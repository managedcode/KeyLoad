using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace KeyLoad.Orleans;

internal static class ReplicaPayloadReader
{
    private const int MaximumKindTokenBytes = 64;
    private const int MaximumJsonEscapeBytes = 6;

    internal static string Name(string property) => JsonNamingPolicy.CamelCase.ConvertName(property);

    internal static int Field(ref Utf8JsonReader reader, string[] names, ref ulong seen)
    {
        Require(reader.TokenType == JsonTokenType.PropertyName);
        for (var position = 0; position < names.Length; position++)
        {
            if (!reader.ValueTextEquals(names[position]))
            {
                continue;
            }

            var mask = 1UL << position;
            Require((seen & mask) == 0);
            seen |= mask;
            Require(reader.Read());
            return position;
        }

        throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
    }

    internal static void CompleteObject(ref Utf8JsonReader reader, string[] fields, ulong seen)
        => Require(reader.TokenType == JsonTokenType.EndObject && seen == (1UL << fields.Length) - 1);

    internal static long Number(ref Utf8JsonReader reader, bool positive = false)
    {
        Require(reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out _));
        var value = reader.GetInt64();
        Require(positive ? value > 0 : value >= 0);
        return value;
    }

    internal static Guid Identifier(ref Utf8JsonReader reader)
    {
        Require(reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out _));
        var value = reader.GetGuid();
        Require(value != Guid.Empty);
        return value;
    }

    internal static void Identity(ref Utf8JsonReader reader)
    {
        Require(reader.TokenType == JsonTokenType.String
            && reader.ValueSpan.Length <= ReplicaTransportProtocol.MaximumIdentityCharacters * MaximumJsonEscapeBytes);
        var value = reader.GetString();
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= ReplicaTransportProtocol.MaximumIdentityCharacters);
    }

    internal static OperationKind Kind(ref Utf8JsonReader reader)
    {
        Require((reader.TokenType is JsonTokenType.String or JsonTokenType.Number) && reader.ValueSpan.Length <= MaximumKindTokenBytes);
        var kind = JsonSerializer.Deserialize<OperationKind>(ref reader, JsonDefaults.Options);
        Require(Enum.IsDefined(kind));
        return kind;
    }

    internal static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }
}
