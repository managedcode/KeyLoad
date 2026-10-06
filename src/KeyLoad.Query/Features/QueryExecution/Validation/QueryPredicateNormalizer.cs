using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Normalizes one bounded predicate tree and its scalar values using frozen operation budgets.</summary>
internal sealed class QueryPredicateNormalizer(int maximumDepth, int maximumNodes, int maximumInValues,
    Func<string, string> normalizeScalarJson)
{
    private const string EmptyFieldAlias = "";
    private const string QueryASTExceedsItsDepthOrNodeBudgetDetail = "The query AST exceeds its depth or node budget.";
    private const string QueryOperandIsUnsupportedDetail = "The query operand is unsupported.";
    private const int AdjacentElementOffset = 1;
    private const int MinimumPositiveCount = 1;
    private const string QueryPredicateIsUnsupportedDetail = "The query predicate is unsupported.";
    private const string QueryComparisonIsUnsupportedDetail = "The query comparison is unsupported.";
    private const string QueryLogicalOperatorIsUnsupportedDetail = "The query logical operator is unsupported.";

    private int count;

    internal JsonElement Scalar(JsonElement value)
    {
        _ = JsonData.Scalar(value, EmptyFieldAlias);
        using var normalized = JsonDocument.Parse(normalizeScalarJson(value.GetRawText()));
        return normalized.RootElement.Clone();
    }

    private void Visit(int depth)
    {
        if (depth > maximumDepth || ++count > maximumNodes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryASTExceedsItsDepthOrNodeBudgetDetail);
        }
    }

    private Operand NormalizeOperand(Operand operand, int depth)
    {
        Visit(depth);
        switch (operand)
        {
            case FieldOperand field:
                QueryValidation.Path(field.Path, maximumDepth);
                return new FieldOperand(field.Path);
            case ValueOperand value:
                return ValueOperand.Create(Scalar(value.Value));
            case ParameterOperand parameter:
                JsonData.Identifier(parameter.Name);
                return new ParameterOperand(parameter.Name);
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryOperandIsUnsupportedDetail);
        }
    }

    internal Predicate Filter(Predicate predicate, int depth)
    {
        Visit(depth);
        return predicate switch
        {
            Comparison comparison => NormalizeComparison(comparison, depth),
            Logical logical => NormalizeLogical(logical, depth),
            Negation negation => new Negation(Filter(negation.Inner, depth + AdjacentElementOffset)),
            NullTest test => new NullTest(NormalizeOperand(test.Value, depth + AdjacentElementOffset), test.Negated, test.Missing),
            InPredicate list when !list.Values.IsDefault && list.Values.Length >= MinimumPositiveCount && list.Values.Length <= maximumInValues
                => new InPredicate(NormalizeOperand(list.Value, depth + AdjacentElementOffset),
                    [.. list.Values.Select(value => NormalizeOperand(value, depth + AdjacentElementOffset))], list.Negated),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryPredicateIsUnsupportedDetail)
        };
    }

    private Comparison NormalizeComparison(Comparison comparison, int depth)
    {
        if (comparison.Operator is not (SqlSyntax.Equals or SqlSyntax.NotEquals or SqlSyntax.AlternateNotEquals
            or SqlSyntax.Greater or SqlSyntax.GreaterOrEqual or SqlSyntax.Less or SqlSyntax.LessOrEqual))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryComparisonIsUnsupportedDetail);
        }
        return new Comparison(NormalizeOperand(comparison.Left, depth + AdjacentElementOffset),
            comparison.Operator == SqlSyntax.AlternateNotEquals ? SqlSyntax.NotEquals : comparison.Operator,
            NormalizeOperand(comparison.Right, depth + AdjacentElementOffset));
    }

    private Logical NormalizeLogical(Logical logical, int depth)
    {
        if (logical.Operator is not (SqlSyntax.And or SqlSyntax.Or))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueryLogicalOperatorIsUnsupportedDetail);
        }
        return new Logical(Filter(logical.Left, depth + AdjacentElementOffset), logical.Operator, Filter(logical.Right, depth + AdjacentElementOffset));
    }
}
