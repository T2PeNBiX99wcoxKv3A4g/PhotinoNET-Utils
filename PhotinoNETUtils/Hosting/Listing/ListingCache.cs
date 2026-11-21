using System.Collections.Concurrent;

namespace PhotinoNetUtils.Hosting.Listing;

/// <summary>
///     The folder a page request is answered from. A listing is taken from the source once and kept
///     with its signature; pages, and each sort of them, are slices of that list. Without it every
///     scroll would walk the disk again.
/// </summary>
internal sealed class ListingCache(IListingSource source, int keep = 6)
{
    private readonly ConcurrentDictionary<string, Task<Entry>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    public async Task<Page> ReadAsync(string path, string sort, bool descending, int from, int count,
        CancellationToken cancellationToken)
    {
        var entry = await LoadAsync(path, cancellationToken);
        var list = Sorted(entry, sort, descending);
        var start = Math.Min(Math.Max(from, 0), list.Count);
        var length = Math.Min(count, list.Count - start);

        return new(entry.Path, entry.Signature, list.Count, list.GetRange(start, length));
    }

    private async Task<Entry> LoadAsync(string path, CancellationToken cancellationToken)
    {
        // A burst of page requests for one folder is answered by a single walk.
        var task = _inflight.GetOrAdd(path, key => LoadCoreAsync(key));

        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        finally
        {
            if (task.IsCompleted)
                _inflight.TryRemove(new KeyValuePair<string, Task<Entry>>(path, task));
        }
    }

    private async Task<Entry> LoadCoreAsync(string path)
    {
        var snapshot = await source.ReadAsync(path, CancellationToken.None);

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var known) && known.Signature == snapshot.Signature)
                return known;

            var entry = new Entry(snapshot.Signature, snapshot.Path, [.. snapshot.Entries]);

            _entries[path] = entry;
            _order.Enqueue(path);

            while (_order.Count > keep)
                _entries.Remove(_order.Dequeue(), out _);

            return entry;
        }
    }

    private List<ListingEntry> Sorted(Entry entry, string sort, bool descending)
    {
        var key = $"{sort}|{(descending ? 1 : 0)}";

        lock (_gate)
        {
            if (entry.Sorted.TryGetValue(key, out var known))
                return known;

            var list = new List<ListingEntry>(entry.Entries);
            list.Sort((left, right) => Compare(left, right, sort, descending));

            entry.Sorted[key] = list;
            return list;
        }
    }

    private static int Compare(ListingEntry left, ListingEntry right, string sort, bool descending)
    {
        // Folders first in both directions, the way Explorer orders a listing.
        if (left.IsDirectory != right.IsDirectory)
            return left.IsDirectory ? -1 : 1;

        var result = sort switch
        {
            "size" => left.Size.CompareTo(right.Size),
            "modified" => left.Modified.CompareTo(right.Modified),
            "created" => left.Created.CompareTo(right.Created),
            "type" => string.Compare(ExtensionOf(left), ExtensionOf(right), StringComparison.OrdinalIgnoreCase),
            _ => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        };

        // Equal keys stay in name order, so a page is stable from one request to the next.
        if (result == 0 && sort != "name")
            result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

        return descending ? -result : result;
    }

    private static string ExtensionOf(ListingEntry entry) =>
        entry.IsDirectory ? "" : Path.GetExtension(entry.Name);

    internal sealed record Page(string Path, int Signature, int Total, IReadOnlyList<ListingEntry> Entries);

    private sealed class Entry(int signature, string path, List<ListingEntry> entries)
    {
        public int Signature { get; } = signature;

        public string Path { get; } = path;

        public List<ListingEntry> Entries { get; } = entries;

        /// <summary>The same list under one sort, so scrolling never re-sorts.</summary>
        public Dictionary<string, List<ListingEntry>> Sorted { get; } = new(StringComparer.Ordinal);
    }
}
