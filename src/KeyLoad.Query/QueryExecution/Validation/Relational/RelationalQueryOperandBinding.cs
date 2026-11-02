using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Resolves schema fields and preserves the executor's existing scalar comparability domains.</summary>
internal sealed class RelationalQueryOperandBinding(Dictionary<string, RelationalColumn> columns,
    Dictionary<string, JsonElement>? parameters)
{
    internal KeyComponentKind Field(string path)
    {
        if (path == SqlSyntax.MetadataIdPath)
        {
            return KeyComponentKind.Text;
        }
        if (path == SqlSyntax.MetadataRevisionPath)
        {
            return KeyComponentKind.FixedPoint;
        }
        var segments = JsonData.PathSegments(path);
        if (segments.Length != 1 || !columns.TryGetValue(segments[0], out var column))
        {
            throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail);
        }
        return column.Type switch
        {
            RelationalColumnType.Text or RelationalColumnType.UtcTimestamp => KeyComponentKind.Text,
            RelationalColumnType.WholeNumber or RelationalColumnType.FixedPoint => KeyComponentKind.FixedPoint,
            RelationalColumnType.Boolean => KeyComponentKind.Boolean,
            _ => throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail)
        };
    }

    internal KeyComponentKind? Infer(Operand operand) => operand switch
    {
        FieldOperand field => Field(field.Path),
        ValueOperand value => Scalar(value.Value),
        ParameterOperand parameter when parameters is not null && parameters.TryGetValue(parameter.Name, out var value)
            => Scalar(value),
        _ => throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail)
    };

    internal void Compare(Operand left, Operand right) => RequireComparable(Infer(left), Infer(right));

    internal static void RequireComparable(KeyComponentKind? left, KeyComponentKind? right)
    {
        if (left is not null && right is not null && left != right)
        {
            throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail);
        }
    }

    private static KeyComponentKind? Scalar(JsonElement value)
    {
        var kind = JsonData.Scalar(value, ReadOnlySpan<string>.Empty).Value.Value.Kind;
        return kind is KeyComponentKind.Null or KeyComponentKind.Missing ? default(KeyComponentKind?) : kind;
    }
}
