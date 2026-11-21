using System.Globalization;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace PhotinoNetUtils.Hosting;

/// <summary>Response helpers for <see cref="DataBlock" />, including the page's total row count.</summary>
[PublicAPI]
public static class DataBlockResults
{
    /// <summary>Sends one page and reports how many rows the block holds in total.</summary>
    public static IResult Block(DataBlock block, int from = 0, int count = int.MaxValue) =>
        BinaryContent.Bytes(block.Build(from, count), DataBlock.ContentType,
            headers: new Dictionary<string, string>
            {
                [DataBlock.TotalHeader] = block.Count.ToString(CultureInfo.InvariantCulture)
            });
}
