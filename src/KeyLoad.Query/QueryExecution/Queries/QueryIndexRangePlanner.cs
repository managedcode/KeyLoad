using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Seeks scalar inequalities through the same ordered index used by equality queries.</summary>
internal static class QueryIndexRangePlanner
{
    private const string IndexSpace = "index";

    internal static (bool HasValue, QueryIndexRange? Value) Create(IndexDefinition index, AstQueryRequest request)
    {
        if (index.Fields.Length != 1 || index.Fields[0] is SqlSyntax.MetadataIdPath or SqlSyntax.MetadataRevisionPath)
        {
            return (false, default(QueryIndexRange?));
        }
        var prefix = KeySpace.Partition(IndexSpace, request.Partition, request.Query.Collection, index.Name);
        byte[]? lower = null;
        byte[]? upper = null;
        KeyComponentKind? scalarType = null;
        var found = false;
        foreach (var comparison in Comparisons(request.Query.Filter))
        {
            var field = comparison.Left as FieldOperand ?? comparison.Right as FieldOperand;
            if (field?.Path != index.Fields[0])
            {
                continue;
            }
            var operation = comparison.Left is FieldOperand ? comparison.Operator : Reverse(comparison.Operator);
            if (operation is not (SqlSyntax.Greater or SqlSyntax.GreaterOrEqual or SqlSyntax.Less or SqlSyntax.LessOrEqual))
            {
                continue;
            }
            var operand = comparison.Left is FieldOperand ? comparison.Right : comparison.Left;
            var value = Value(operand, request.Parameters).Value;
            if (value.Kind is KeyComponentKind.Null or KeyComponentKind.Missing)
            {
                continue;
            }
            if (scalarType is not null && scalarType != value.Kind)
            {
                return (false, default(QueryIndexRange?));
            }
            scalarType = value.Kind;
            var key = KeySpace.Partition(IndexSpace, request.Partition, request.Query.Collection, index.Name, value);
            if (operation is SqlSyntax.Greater or SqlSyntax.GreaterOrEqual)
            {
                var bound = operation == SqlSyntax.Greater ? StorageKeyBounds.PrefixSuccessor(key).Value : key;
                lower = lower is null || bound is not null && lower.AsSpan().SequenceCompareTo(bound) < 0 ? bound : lower;
                found = true;
            }
            if (operation is SqlSyntax.Less or SqlSyntax.LessOrEqual)
            {
                var bound = operation == SqlSyntax.LessOrEqual ? StorageKeyBounds.PrefixSuccessor(key).Value : key;
                upper = upper is null || bound is not null && upper.AsSpan().SequenceCompareTo(bound) > 0 ? bound : upper;
                found = true;
            }
        }
        return found ? (true, new(prefix, lower, upper)) : (false, default(QueryIndexRange?));
    }

    private static (bool HasValue, KeyComponent Value) Value(Operand operand, Dictionary<string, JsonElement>? parameters)
        => operand switch
        {
            ValueOperand value => (true, JsonData.Scalar(value.Value, string.Empty)),
            ParameterOperand parameter when parameters?.TryGetValue(parameter.Name, out var value) == true
                => (true, JsonData.Scalar(value, string.Empty)),
            _ => (false, default(KeyComponent))
        };

    private static string Reverse(string operation) => operation switch
    {
        SqlSyntax.Greater => SqlSyntax.Less,
        SqlSyntax.GreaterOrEqual => SqlSyntax.LessOrEqual,
        SqlSyntax.Less => SqlSyntax.Greater,
        SqlSyntax.LessOrEqual => SqlSyntax.GreaterOrEqual,
        _ => operation
    };

    private static IEnumerable<Comparison> Comparisons(Predicate? predicate)
    {
        if (predicate is Logical { Operator: SqlSyntax.And } conjunction)
        {
            foreach (var comparison in Comparisons(conjunction.Left).Concat(Comparisons(conjunction.Right)))
            {
                yield return comparison;
            }
        }
        else if (predicate is Comparison comparison)
        {
            yield return comparison;
        }
    }
}
