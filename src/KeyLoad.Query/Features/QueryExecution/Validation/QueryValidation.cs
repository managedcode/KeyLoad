using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class QueryValidation
{
    private const int MaximumProjection = 256;
    private const int MaximumOrdering = 16;
    private const int MaximumParameters = 256;
    private const int MaximumInValues = 256;
    private const string InvalidPath = "The query field path is invalid.";

    public static AstQueryRequest Normalize(AstQueryRequest request, DatabaseLimits limits)
    {
        if (request is null || request.Partition is null || request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, "A query request, partition and query are required.");
        }
        if (request.AstVersion != 1)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query AST version is unsupported.");
        }

        var query = request.Query;
        DatabaseEngine.ValidatePartition(request.Partition);
        JsonData.Identifier(query.Collection);
        ValidateModelSource(request, query);
        if (query.Projection.IsDefault || query.Order.IsDefault
            || query.Projection is not { Length: >= 1 and <= MaximumProjection }
            || query.Order.Length > MaximumOrdering || query.Limit < 1 || query.Limit > limits.MaxResults
            || request.Parameters?.Count > MaximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The query structure exceeds its budget.");
        }

        var projection = NormalizeProjection(query.Projection, limits);
        var order = NormalizeOrder(query.Order, limits);
        var predicates = new PredicateNormalizer(limits);
        var filter = query.Filter is null ? null : predicates.Filter(query.Filter, 1);
        var parameters = NormalizeParameters(request.Parameters, predicates);
        var normalized = request with
        {
            Query = query with { Alias = null, Projection = projection, Order = order, Filter = filter },
            Parameters = parameters
        };
        if (JsonDefaults.Serialize(normalized).Length > limits.MaxQueryBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The query request exceeds its byte budget.");
        }
        return normalized;
    }

    private static void ValidateModelSource(AstQueryRequest request, SelectQuery query)
    {
        if (query.ModelSource is not { } source)
        {
            return;
        }
        if (!Enum.IsDefined(source.Kind))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "The model query source is unsupported.");
        }
        JsonData.Identifier(source.Item);
        if (!request.AllowFullScan)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "A model query requires explicit full-scan consent.");
        }
        if (request.Cursor is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "Model query continuation is not supported.");
        }
        if (source.Kind == ModelQuerySourceKind.Events && source.Generation < 1
            || source.Kind == ModelQuerySourceKind.QueueMessages
            && (source.Generation != 1 || source.Item != query.Collection))
        {
            throw Errors.Fail(ErrorCode.Validation, "The model query source arguments are invalid.");
        }
    }

    private static ImmutableArray<Selection> NormalizeProjection(ImmutableArray<Selection> input, DatabaseLimits limits)
    {
        var projection = input.Select(selection =>
        {
            if (selection is null)
            {
                throw Errors.Fail(ErrorCode.Validation, "A query projection entry is missing.");
            }
            Path(selection.Path, limits, true);
            JsonData.Identifier(selection.Alias);
            return selection with { };
        }).ToImmutableArray();
        if (projection.Select(s => s.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Length
            || projection.Length > 1 && projection.Any(s => s.Path == "*"))
        {
            throw Errors.Fail(ErrorCode.Validation, "The query projection has duplicate aliases or an invalid star.");
        }
        return projection;
    }

    private static ImmutableArray<Ordering> NormalizeOrder(ImmutableArray<Ordering> input, DatabaseLimits limits)
        => input.Select(item =>
        {
            if (item is null)
            {
                throw Errors.Fail(ErrorCode.Validation, "A query ordering entry is missing.");
            }
            Path(item.Path, limits);
            return item with { };
        }).ToImmutableArray();

    private static Dictionary<string, JsonElement>? NormalizeParameters(Dictionary<string, JsonElement>? parameters,
        PredicateNormalizer predicates) => parameters is not { Count: > 0 } ? null : parameters.ToDictionary(item =>
    {
        JsonData.Identifier(item.Key);
        return item.Key;
    }, item => predicates.Scalar(item.Value), StringComparer.Ordinal);

    private static void Path(string path, DatabaseLimits limits, bool star = false)
    {
        if (star && path == "*")
        {
            return;
        }
        if (string.IsNullOrEmpty(path) || JsonData.PathSegments(path).Length > limits.MaxQueryDepth)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPath);
        }
    }

    private sealed class PredicateNormalizer(DatabaseLimits limits)
    {
        private int count;

        internal JsonElement Scalar(JsonElement value)
        {
            _ = JsonData.Scalar(value, "");
            using var normalized = JsonDocument.Parse(JsonData.Validate(value.GetRawText(), limits, false));
            return normalized.RootElement.Clone();
        }

        private void Visit(int depth)
        {
            if (depth > limits.MaxQueryDepth || ++count > limits.MaxQueryTokens)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The query AST exceeds its depth or node budget.");
            }
        }

        private Operand NormalizeOperand(Operand operand, int depth)
        {
            Visit(depth);
            switch (operand)
            {
                case FieldOperand field:
                    Path(field.Path, limits);
                    return new FieldOperand(field.Path);
                case ValueOperand value:
                    return ValueOperand.Create(Scalar(value.Value));
                case ParameterOperand parameter:
                    JsonData.Identifier(parameter.Name);
                    return new ParameterOperand(parameter.Name);
                default:
                    throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query operand is unsupported.");
            }
        }

        internal Predicate Filter(Predicate predicate, int depth)
        {
            Visit(depth);
            return predicate switch
            {
                Comparison comparison => NormalizeComparison(comparison, depth),
                Logical logical => NormalizeLogical(logical, depth),
                Negation negation => new Negation(Filter(negation.Inner, depth + 1)),
                NullTest test => new NullTest(NormalizeOperand(test.Value, depth + 1), test.Negated, test.Missing),
                InPredicate list when !list.Values.IsDefault && list.Values is { Length: >= 1 and <= MaximumInValues }
                    => new InPredicate(NormalizeOperand(list.Value, depth + 1),
                        [.. list.Values.Select(value => NormalizeOperand(value, depth + 1))], list.Negated),
                _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query predicate is unsupported.")
            };
        }

        private Comparison NormalizeComparison(Comparison comparison, int depth)
        {
            if (comparison.Operator is not ("=" or "!=" or "<>" or ">" or ">=" or "<" or "<="))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query comparison is unsupported.");
            }
            return new Comparison(NormalizeOperand(comparison.Left, depth + 1),
                comparison.Operator == "<>" ? "!=" : comparison.Operator, NormalizeOperand(comparison.Right, depth + 1));
        }

        private Logical NormalizeLogical(Logical logical, int depth)
        {
            if (logical.Operator is not ("AND" or "OR"))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query logical operator is unsupported.");
            }
            return new Logical(Filter(logical.Left, depth + 1), logical.Operator, Filter(logical.Right, depth + 1));
        }
    }
}
