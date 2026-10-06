using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Private structural schema options; canonical request and result serialization never uses this projection.</summary>
internal static class McpSchemaProjection
{
    private const int RequiredFamilyMatches = 1;

    /// <summary>Copies canonical options while replacing only the two opaque strict format converter families with native metadata.</summary>
    /// <returns>A read-only options copy used exclusively by the JSON Schema exporter.</returns>
    internal static JsonSerializerOptions Create()
    {
        const int ByteMatchesInitialValue = 0;
        const int ArrayMatchesInitialValue = 0;
        const int IndexValidationBoundary = 0;

        var options = new JsonSerializerOptions(JsonDefaults.Options);
        var byteMatches = ByteMatchesInitialValue;
        var arrayMatches = ArrayMatchesInitialValue;
        for (var index = options.Converters.Count - RequiredFamilyMatches; index >= IndexValidationBoundary; index--)
        {
            var converter = options.Converters[index];
            var bytes = converter.CanConvert(typeof(ReadOnlyMemory<byte>));
            var arrays = converter.CanConvert(typeof(ImmutableArray<Mutation>));
            if (bytes && arrays)
            { throw new InvalidOperationException(McpCatalogProtocol.ConverterConfiguration); }
            if (bytes)
            { byteMatches++; }
            if (arrays)
            { arrayMatches++; }
            if (bytes || arrays)
            { options.Converters.RemoveAt(index); }
        }
        if (byteMatches != RequiredFamilyMatches || arrayMatches != RequiredFamilyMatches)
        {
            throw new InvalidOperationException(McpCatalogProtocol.ConverterConfiguration);
        }
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
