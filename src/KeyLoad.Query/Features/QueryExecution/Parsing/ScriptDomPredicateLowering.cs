using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomPredicateLowering
{
    internal static Predicate Read(Dom.BooleanExpression expression, string? alias, string? rightAlias,
        ScriptDomLoweringContext context, int depth = RootDepth)
    {
        context.Check(depth);
        Operand Operand(Dom.ScalarExpression value) => ScriptDomOperandLowering.Read(value, alias, rightAlias, context, depth + NextDepth);
        Predicate Predicate(Dom.BooleanExpression value) => Read(value, alias, rightAlias, context, depth + NextDepth);
        return expression switch
        {
            Dom.BooleanParenthesisExpression grouped => Predicate(grouped.Expression),
            Dom.BooleanNotExpression negated => new Negation(Predicate(negated.Expression)),
            Dom.BooleanBinaryExpression binary => new Logical(Predicate(binary.FirstExpression),
                binary.BinaryExpressionType == Dom.BooleanBinaryExpressionType.And ? AndOperator : OrOperator, Predicate(binary.SecondExpression)),
            Dom.DistinctPredicate distinct => SqlDistinctPredicate.Lower(Operand(distinct.FirstExpression),
                Operand(distinct.SecondExpression), distinct.IsNot),
            Dom.BooleanComparisonExpression comparison => Compare(comparison, Operand),
            Dom.BooleanIsNullExpression test => new NullTest(Operand(test.Expression), test.IsNot, false),
            Dom.BooleanTernaryExpression range => Range(range, Operand),
            Dom.InPredicate membership => Membership(membership, Operand, context),
            _ => throw ScriptDomErrors.Unsupported()
        };
    }
    private static Predicate Compare(Dom.BooleanComparisonExpression comparison, Func<Dom.ScalarExpression, Operand> operand)
    {
        if (comparison.ComparisonType is Dom.BooleanComparisonType.IsDistinctFrom or Dom.BooleanComparisonType.IsNotDistinctFrom)
        {
            return SqlDistinctPredicate.Lower(operand(comparison.FirstExpression), operand(comparison.SecondExpression),
                comparison.ComparisonType == Dom.BooleanComparisonType.IsNotDistinctFrom);
        }
        return new Comparison(operand(comparison.FirstExpression), Comparison(comparison.ComparisonType),
            operand(comparison.SecondExpression));
    }
    private static Predicate Range(Dom.BooleanTernaryExpression range, Func<Dom.ScalarExpression, Operand> operand)
    {
        if (range.TernaryExpressionType is not (Dom.BooleanTernaryExpressionType.Between or Dom.BooleanTernaryExpressionType.NotBetween))
        { throw ScriptDomErrors.Unsupported(); }
        return SqlBetweenPredicate.Lower(operand(range.FirstExpression), operand(range.SecondExpression),
            operand(range.ThirdExpression), range.TernaryExpressionType == Dom.BooleanTernaryExpressionType.NotBetween, false);
    }
    private static InPredicate Membership(Dom.InPredicate membership, Func<Dom.ScalarExpression, Operand> operand,
        ScriptDomLoweringContext context)
    {
        if (membership.Subquery is not null)
        { throw ScriptDomErrors.Unsupported(); }
        if (membership.Values.Count == EmptyCount || membership.Values.Count > context.Execution.MaximumInValues)
        { throw ScriptDomErrors.Invalid(); }
        return new InPredicate(operand(membership.Expression), [.. membership.Values.Select(operand)], membership.NotDefined);
    }
    internal static string Comparison(Dom.BooleanComparisonType type) => type switch
    {
        Dom.BooleanComparisonType.Equals => EqualOperator,
        Dom.BooleanComparisonType.NotEqualToBrackets => UnequalBracketOperator,
        Dom.BooleanComparisonType.NotEqualToExclamation => UnequalBangOperator,
        Dom.BooleanComparisonType.GreaterThan => GreaterOperator,
        Dom.BooleanComparisonType.GreaterThanOrEqualTo => GreaterOrEqualOperator,
        Dom.BooleanComparisonType.LessThan => LessOperator,
        Dom.BooleanComparisonType.LessThanOrEqualTo => LessOrEqualOperator,
        Dom.BooleanComparisonType.NotLessThan => GreaterOrEqualOperator,
        Dom.BooleanComparisonType.NotGreaterThan => LessOrEqualOperator,
        _ => throw ScriptDomErrors.Unsupported()
    };

    private const string AndOperator = "AND";
    private const string OrOperator = "OR";
    private const string EqualOperator = "=";
    private const string UnequalBracketOperator = "<>";
    private const string UnequalBangOperator = "!=";
    private const string GreaterOperator = ">";
    private const string GreaterOrEqualOperator = ">=";
    private const string LessOperator = "<";
    private const string LessOrEqualOperator = "<=";

    private const int EmptyCount = 0;
    private const int RootDepth = 1;
    private const int NextDepth = 1;
}
