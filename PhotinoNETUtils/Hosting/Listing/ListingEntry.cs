using JetBrains.Annotations;

namespace PhotinoNetUtils.Hosting.Listing;

/// <summary>
///     One row of a folder listing, as the data plane carries it. The app maps its own entry type to
///     this, so the block layout and the route stay in the library while the filesystem walk stays in
///     the app.
/// </summary>
/// <remarks>
///     <paramref name="FullPath" /> is deliberately absent: it is the folder's path plus the name,
///     and sending it per entry would repeat the same prefix thousands of times.
/// </remarks>
[PublicAPI]
public readonly record struct ListingEntry(
    string Name,
    long Size,
    long Modified,
    long Created,
    bool IsDirectory,
    bool IsHidden,
    bool IsSystem,
    bool IsEncrypted = false,
    bool? LinkValid = null,
    int LinkCount = 1,
    string? IconSource = null,
    string? LinkTarget = null);

/// <summary>A whole folder as the source hands it over; the route cuts pages out of it.</summary>
[PublicAPI]
public sealed record ListingSnapshot(string Path, int Signature, IReadOnlyList<ListingEntry> Entries);

/// <summary>
///     Where the listing route gets a folder from. The app walks the disk (or reads its index) and
///     answers with the complete listing; paging, sorting, caching and the block layout are the
///     library's business.
/// </summary>
[PublicAPI]
public interface IListingSource
{
    /// <summary>
    ///     Every entry of <paramref name="path" />, hidden and system ones included, in any order.
    ///     <paramref name="signature" /> must change whenever the content does: it is the ETag the
    ///     window revalidates with.
    /// </summary>
    Task<ListingSnapshot> ReadAsync(string path, CancellationToken cancellationToken);
}
