using System.Collections;
using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateInExpression
{
    internal static Predicate Translate(Expression set, Expression candidate, ParameterExpression parameter, QueryTranslationContext context)
    {
        if (set is not null && candidate is not null && context.Expressions.Constant(set, out var values) && IsSupportedCollection(values))
        {
            return Create(values!, candidate, parameter, context);
        }

        throw QueryExpressions.Unsupported();
    }

    internal static bool TryGetCollection(MethodCallExpression call, out Expression set, out Expression candidate)
    {
        if (IsEnumerableContains(call))
        {
            set = call.Arguments[QueryPredicateTokens.CollectionArgumentIndex];
            candidate = call.Arguments[QueryPredicateTokens.CandidateArgumentIndex];
            return true;
        }

        if (IsSpanContains(call))
        {
            var conversion = (MethodCallExpression)call.Arguments[QueryPredicateTokens.CollectionArgumentIndex];
            set = conversion.Arguments[QueryPredicateTokens.CollectionArgumentIndex];
            candidate = call.Arguments[QueryPredicateTokens.CandidateArgumentIndex];
            return true;
        }

        if (IsListContains(call))
        {
            set = call.Object!;
            candidate = call.Arguments[QueryPredicateTokens.CollectionArgumentIndex];
            return true;
        }

        set = null!;
        candidate = null!;
        return false;
    }

    private static bool IsEnumerableContains(MethodCallExpression call) => call.Method.DeclaringType == typeof(Enumerable)
        && call.Method.Name == nameof(Enumerable.Contains)
        && call.Arguments.Count == QueryPredicateTokens.ContainsArgumentCount;

    private static bool IsSpanContains(MethodCallExpression call)
    {
        if (call.Method.DeclaringType != typeof(MemoryExtensions) || call.Method.Name != nameof(MemoryExtensions.Contains)
            || call.Arguments.Count != QueryPredicateTokens.ContainsArgumentCount || call.Arguments[QueryPredicateTokens.CollectionArgumentIndex] is not MethodCallExpression conversion)
        {
            return false;
        }

        return conversion.Method.Name == QueryPredicateTokens.ImplicitConversionOperatorName
            && conversion.Method.DeclaringType is { IsGenericType: true } span
            && span.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>)
            && conversion.Arguments.Count == QueryPredicateTokens.MarkerArgumentCount
            && conversion.Arguments[QueryPredicateTokens.CollectionArgumentIndex].Type.IsArray;
    }

    private static bool IsListContains(MethodCallExpression call) => call.Method.DeclaringType is { IsGenericType: true } list
        && list.GetGenericTypeDefinition() == typeof(List<>)
        && call.Method.Name == nameof(List<int>.Contains)
        && call.Arguments.Count == QueryPredicateTokens.MarkerArgumentCount;

    private static bool IsSupportedCollection(object? values) => values is not null
        && (values is Array || values.GetType().IsGenericType && values.GetType().GetGenericTypeDefinition() == typeof(List<>));

    private static InPredicate Create(object values, Expression candidate, ParameterExpression parameter, QueryTranslationContext context)
    {
        var operands = new List<Operand>();
        foreach (var item in (IEnumerable)values)
        {
            if (operands.Count == context.Limits.MaximumInItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.InListBudgetExceededMessage);
            }

            operands.Add(QueryExpressions.Literal(item));
        }

        if (operands.Count == QueryPredicateTokens.EmptyCount)
        {
            throw QueryExpressions.Unsupported();
        }

        var value = context.Expressions.Operand(candidate, parameter);
        return new InPredicate(value, [.. operands], false);
    }
}
