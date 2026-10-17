using System.Text.Json;

namespace KeyLoad.Core.Features.RelationalStorage;

internal static class RelationalScalarValidation
{
    private const string UtcSuffix = "Z";
    private const string ZeroOffsetSuffix = "+00:00";
    private const string InvalidValue = "A relational column value does not match its type or nullability.";

    internal static void Require(RelationalColumn column, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null && column.Nullable)
        {
            return;
        }
        var valid = column.Type switch
        {
            RelationalColumnType.Text => value.ValueKind == JsonValueKind.String,
            RelationalColumnType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            RelationalColumnType.WholeNumber => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            RelationalColumnType.FixedPoint => value.ValueKind == JsonValueKind.Number && RelationalDecimalValidation.IsExact(value),
            RelationalColumnType.UtcTimestamp => IsUtcTimestamp(value),
            _ => false
        };
        if (!valid)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidValue);
        }
    }

    private static bool IsUtcTimestamp(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || !value.TryGetDateTimeOffset(out var timestamp) || timestamp.Offset != TimeSpan.Zero)
        {
            return false;
        }
        var text = value.GetString()!;
        return text.EndsWith(UtcSuffix, StringComparison.Ordinal) || text.EndsWith(ZeroOffsetSuffix, StringComparison.Ordinal);
    }
}
