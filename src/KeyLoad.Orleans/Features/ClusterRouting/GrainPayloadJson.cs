using System.Text;
using System.Text.Json;

namespace KeyLoad.Orleans;

internal static class GrainPayloadJson
{
    private const string NullJson = "null";
    internal static UTF8Encoding Utf8 { get; } = new(false, true);
    private static readonly byte[] NullBytes = Utf8.GetBytes(NullJson);
    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options) { AllowDuplicateProperties = false };

    internal static void Validate(ReadOnlySpan<byte> payload)
    {
        try
        {
            Utf8.GetCharCount(payload);
            var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = Options.MaxDepth });
            if (!reader.Read())
            {
                throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
            }

            while (reader.Read())
            {
            }
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal static T Read<T>(ReadOnlyMemory<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload.Span, Options)
                ?? throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal static void RequireNull(ReadOnlyMemory<byte> payload)
    {
        if (!payload.Span.SequenceEqual(NullBytes))
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
