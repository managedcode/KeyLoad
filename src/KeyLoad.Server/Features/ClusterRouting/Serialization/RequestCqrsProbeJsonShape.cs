using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeJsonShape
{
    internal static void Require(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> fields,
        IOptions<RequestProbeExecutionOptions> executionOptions, UTF8Encoding strictUtf8)
    {
        const int IndexOfValidationBoundary = 0;

        if (bytes.IsEmpty || bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        try
        { _ = strictUtf8.GetCharCount(bytes); }
        catch (EncoderFallbackException)
        { throw Invalid(); }
        catch (DecoderFallbackException)
        { throw Invalid(); }
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = executionOptions.Value.MaximumJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        { throw Invalid(); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
            { throw Invalid(); }
            var name = reader.GetString();
            if (name is null || fields.IndexOf(name) < IndexOfValidationBoundary || !seen.Add(name) || !reader.Read())
            { throw Invalid(); }
            if (name == nameof(RequestCqrsProbeArmRecord.Partition) && reader.TokenType == JsonTokenType.StartObject)
            { RequestCqrsProbePartitionShape.Read(ref reader, executionOptions.Value.MaximumPrincipalBytes); }
            else if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject
                or JsonTokenType.EndArray or JsonTokenType.EndObject)
            { throw Invalid(); }
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != fields.Length)
        { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeJson.InvalidRecord);
}
