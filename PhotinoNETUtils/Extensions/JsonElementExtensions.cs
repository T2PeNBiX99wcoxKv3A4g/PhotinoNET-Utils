using System.Text.Json;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Extensions;

/// <summary>Indexing sugar for the raw JSON a bridge handler sometimes only needs one field of.</summary>
[PublicAPI]
public static class JsonElementExtensions
{
    extension(JsonElement element)
    {
        /// <summary>
        ///     The property with this name — <see cref="JsonElement.GetProperty(string)" /> with indexer
        ///     syntax: <c>payload["direction"]</c>.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No property with this name exists.</exception>
        public JsonElement this[string name] => element.GetProperty(name);
    }
}
