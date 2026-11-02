using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.ResourceExecution;

internal sealed class StrictImmutableArrayJsonConverter<T> : JsonConverter<ImmutableArray<T>>
{
    private const string InvalidCollectionMessage = "A required collection must be an initialized JSON array.";

    public override bool HandleNull => true;

    public override ImmutableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: The CLR guard validates the canonical JSON options reference before decoding the typed backing array. */;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(InvalidCollectionMessage);
        }

        var values = JsonSerializer.Deserialize<T[]>(ref reader, options)
            ?? throw new JsonException(InvalidCollectionMessage);
        // The serializer created this private array; no mutable alias escapes the converter.
        return ImmutableCollectionsMarshal.AsImmutableArray(values);
    }

    public override void Write(Utf8JsonWriter writer, ImmutableArray<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer) /* KLD0041: The CLR guard validates the native JSON writer reference before serializing the typed backing array. */;
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: The CLR guard validates the canonical JSON options reference before encoding the typed backing array. */;
        var values = ImmutableCollectionsMarshal.AsArray(value)
            ?? throw new JsonException(InvalidCollectionMessage);
        // The official serializer only reads this owned backing array.
        JsonSerializer.Serialize(writer, values, options);
    }
}
