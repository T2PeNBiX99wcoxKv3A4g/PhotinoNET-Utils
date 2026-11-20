using JetBrains.Annotations;

namespace PhotinoNetUtils.Hosting;

/// <summary>
///     The <c>--listing-dump</c> command every app gets with the data plane: it asks the source for a
///     folder, encodes the page with the real writer and prints the bytes back as text. A layout change
///     that broke the reader shows up here without a window, a browser or a client.
/// </summary>
[PublicAPI]
public static class ListingDump
{
    private const string Usage =
        "usage: --listing-dump <folder> [--from n] [--count n] [--sort name|size|modified|created|type]";

    /// <summary>
    ///     Runs the dump and returns the process exit code: 0 when it printed a page, 1 when the folder
    ///     could not be read, 2 when the arguments were wrong. <paramref name="arguments" /> is what
    ///     follows the flag.
    /// </summary>
    public static async Task<int> RunAsync(IListingSource source, IReadOnlyList<string> arguments,
        TextWriter output)
    {
        if (arguments.Count == 0)
        {
            output.WriteLine(Usage);
            return 2;
        }

        var path = arguments[0];
        var from = 0;
        var count = 20;
        var sort = "name";

        for (var i = 1; i + 1 < arguments.Count; i += 2)
            switch (arguments[i])
            {
                case "--from":
                    _ = int.TryParse(arguments[i + 1], out from);
                    break;

                case "--count":
                    _ = int.TryParse(arguments[i + 1], out count);
                    break;

                case "--sort":
                    sort = arguments[i + 1];
                    break;
            }

        try
        {
            var cache = new ListingCache(source);
            var page = await cache.ReadAsync(path, sort, false, from, Math.Max(1, count), CancellationToken.None);
            var block = ListingFormat.Encode(page.Entries, page.Path);

            output.WriteLine(
                $"block {block.Length} bytes · total {page.Total} · signature {page.Signature:x8}");
            output.Write(ListingFormat.Dump(block, page.Total));

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.Message);
            return 1;
        }
    }
}
