using JetBrains.Annotations;

namespace PhotinoNetUtils.Hosting.Listing;

/// <summary>
///     Names an app can put on the listing endpoint without the library knowing the app. The
///     defaults are the neutral ones <see cref="ListingRoute" /> and <see cref="ListingFormat" />
///     publish; an app that already has a client contract overrides the pieces it needs.
/// </summary>
[PublicAPI]
public sealed record ListingRouteOptions
{
    /// <summary>The paths and wire names used when an app does not supply any.</summary>
    public static ListingRouteOptions Default { get; } = new();

    /// <summary>
    ///     The route under the data plane's <c>/data/&lt;token&gt;/</c> prefix. A leading or trailing
    ///     slash is ignored; an empty value maps the prefix itself.
    /// </summary>
    public string Route { get; init; } = "list";

    /// <summary>The media type a binary page is served with.</summary>
    public string ContentType { get; init; } = ListingFormat.ContentType;

    /// <summary>The response header that carries the folder's total entry count.</summary>
    public string TotalHeader { get; init; } = ListingFormat.TotalHeader;

    /// <summary>How many entries a request without <c>count</c> receives.</summary>
    public int DefaultCount { get; init; } = 400;

    /// <summary>The largest <c>count</c> a request may ask for.</summary>
    public int MaxCount { get; init; } = 100_000;
}