using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class QueryValidation
{
    public static AstQueryRequest Normalize(AstQueryRequest request, DatabaseLimits limits)
    {
        if (request is null || request.Partition is null || request.Query is null)
            throw Errors.Fail(ErrorCode.Validation, "A query request, partition and query are required.");
        if (request.AstVersion != 1) throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query AST version is unsupported.");
        var query = request.Query; DatabaseEngine.ValidatePartition(request.Partition); JsonData.Identifier(query.Collection);
        if (query.Projection is not { Length: >= 1 and <= 256 } || query.Order is not { Length: <= 16 }
            || query.Limit < 1 || query.Limit > limits.MaxResults || request.Parameters?.Count > 256)
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The query structure exceeds its budget.");
        void Path(string path, bool star = false)
        {
            if (star && path == "*") return;
            if (string.IsNullOrEmpty(path) || JsonData.PathSegments(path).Length > limits.MaxQueryDepth)
                throw Errors.Fail(ErrorCode.Validation, "The query field path is invalid.");
        }
        var projection = query.Projection.Select(selection =>
        {
            if (selection is null) throw Errors.Fail(ErrorCode.Validation, "A query projection entry is missing.");
            Path(selection.Path, true); JsonData.Identifier(selection.Alias); return selection with { };
        }).ToArray();
        if (projection.Select(s => s.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Length
            || projection.Length > 1 && projection.Any(s => s.Path == "*"))
            throw Errors.Fail(ErrorCode.Validation, "The query projection has duplicate aliases or an invalid star.");
        var order = query.Order.Select(item =>
        {
            if (item is null) throw Errors.Fail(ErrorCode.Validation, "A query ordering entry is missing.");
            Path(item.Path); return item with { };
        }).ToArray();
        var count = 0;
        void Visit(int depth)
        {
            if (depth > limits.MaxQueryDepth || ++count > limits.MaxQueryTokens)
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The query AST exceeds its depth or node budget.");
        }
        JsonElement Scalar(JsonElement value)
        {
            _ = JsonData.Scalar(value, "");
            using var normalized = JsonDocument.Parse(JsonData.Validate(value.GetRawText(), limits, false));
            return normalized.RootElement.Clone();
        }
        Operand Operand(Operand operand, int depth)
        {
            Visit(depth);
            switch (operand)
            {
                case FieldOperand field: Path(field.Path); return field with { };
                case ValueOperand value: return new ValueOperand(Scalar(value.Value));
                case ParameterOperand parameter: JsonData.Identifier(parameter.Name); return parameter with { };
                default: throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query operand is unsupported.");
            }
        }
        Predicate Filter(Predicate predicate, int depth)
        {
            Visit(depth);
            switch (predicate)
            {
                case Comparison comparison:
                    if (comparison.Operator is not ("=" or "!=" or "<>" or ">" or ">=" or "<" or "<="))
                        throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query comparison is unsupported.");
                    return new Comparison(Operand(comparison.Left, depth + 1), comparison.Operator == "<>" ? "!=" : comparison.Operator,
                        Operand(comparison.Right, depth + 1));
                case Logical logical:
                    if (logical.Operator is not ("AND" or "OR")) throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query logical operator is unsupported.");
                    return new Logical(Filter(logical.Left, depth + 1), logical.Operator, Filter(logical.Right, depth + 1));
                case Negation negation: return new Negation(Filter(negation.Inner, depth + 1));
                case NullTest test: return new NullTest(Operand(test.Value, depth + 1), test.Negated, test.Missing);
                case InPredicate list when list.Values is { Length: >= 1 and <= 256 }:
                    return new InPredicate(Operand(list.Value, depth + 1), list.Values.Select(value => Operand(value, depth + 1)).ToArray(), list.Negated);
                default: throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query predicate is unsupported.");
            }
        }
        var filter = query.Filter is null ? null : Filter(query.Filter, 1);
        var parameters = request.Parameters is not { Count: > 0 } ? null : request.Parameters.ToDictionary(item =>
        {
            JsonData.Identifier(item.Key); return item.Key;
        }, item => Scalar(item.Value), StringComparer.Ordinal);
        var normalized = request with { Query = query with { Alias = null, Projection = projection, Order = order, Filter = filter }, Parameters = parameters };
        if (JsonDefaults.Serialize(normalized).Length > limits.MaxQueryBytes)
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The query request exceeds its byte budget.");
        return normalized;
    }
}
