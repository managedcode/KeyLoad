using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Features.ResourceExecution;

internal sealed class StrictByteMemoryJsonConverter : JsonConverter<ReadOnlyMemory<byte>>
{
    private const string InvalidBufferMessage = "A required byte buffer must be a base64 JSON string.";

    public override bool HandleNull => true;

    public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetBytesFromBase64(out var bytes) || bytes is null)
        {
            throw new JsonException(InvalidBufferMessage);
        }

        return bytes;
    }

    public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBase64StringValue(value.Span);
    }
}
