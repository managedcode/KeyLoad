using KeyLoad.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal static class ScriptDomNullPredicateLowering
{
    internal static Predicate Read(Dom.BooleanIsNullExpression test, Operand value,
        ScriptDomLoweringContext context, int depth)
    {
        return HasOriginalDistinct(test, context, depth)
            ? SqlDistinctPredicate.Lower(value, ValueOperand.Create(null), !test.IsNot)
            : new NullTest(value, test.IsNot, false);
    }

    private static bool HasOriginalDistinct(Dom.BooleanIsNullExpression test,
        ScriptDomLoweringContext context, int depth)
    {
        var tokens = test.ScriptTokenStream;
        var operandEnd = test.Expression.LastTokenIndex;
        if (tokens is null || test.FirstTokenIndex < FirstTokenIndex ||
            operandEnd < test.FirstTokenIndex || operandEnd >= test.LastTokenIndex ||
            test.LastTokenIndex >= tokens.Count)
        {
            throw ScriptDomErrors.Unsupported();
        }

        // ScriptDOM folds DISTINCT FROM NULL into BooleanIsNullExpression. Inspect
        // only the original execution operator span, never operand text or trivia text.
        for (var index = operandEnd + 1; index <= test.LastTokenIndex; index++)
        {
            context.Check(depth);
            if (tokens[index].TokenType == Dom.TSqlTokenType.Distinct)
            {
                return true;
            }
        }
        return false;
    }

    private const int FirstTokenIndex = 0;
}
