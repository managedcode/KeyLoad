using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Resolves schema fields and preserves the executor's existing scalar comparability domains.</summary>
internal sealed class RelationalQueryOperandBinding(Dictionary<string, RelationalColumn> columns,
    Dictionary<string, JsonElement>? parameters)
{
    internal Type Field(string path)
    {
        if (path == SqlSyntax.MetadataIdPath)
        {
            return typeof(string);
        }
        if (path == SqlSyntax.MetadataRevisionPath)
        {
            return typeof(decimal);
        }
        var segments = JsonData.PathSegments(path);
        if (segments.Length != 1 || !columns.TryGetValue(segments[0], out var column))
        {
            throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail);
        }
        return column.Type switch
        {
            RelationalColumnType.Text or RelationalColumnType.UtcTimestamp => typeof(string),
            RelationalColumnType.WholeNumber or RelationalColumnType.FixedPoint => typeof(decimal),
            RelationalColumnType.Boolean => typeof(bool),
            _ => throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail)
        };
    }

    internal Type? Infer(Operand operand) => operand switch
    {
        FieldOperand field => Field(field.Path),
        ValueOperand value => Scalar(value.Value),
        ParameterOperand parameter when parameters is not null && parameters.TryGetValue(parameter.Name, out var value)
            => Scalar(value),
        _ => throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail)
    };

    internal void Compare(Operand left, Operand right) => RequireComparable(Infer(left), Infer(right));

    internal static void RequireComparable(Type? left, Type? right)
    {
        if (left is not null && right is not null && left != right)
        {
            throw Errors.Fail(ErrorCode.Validation, RelationalQueryBinding.InvalidDetail);
        }
    }

    private static Type? Scalar(JsonElement value) => JsonData.Scalar(value, ReadOnlySpan<string>.Empty)?.GetType();
}
