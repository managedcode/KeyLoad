using System.Collections;
using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateInExpression
{
    internal static Predicate Translate(Expression set, Expression candidate, ParameterExpression parameter, QueryTranslationContext context)
    {
        if (set is not null && candidate is not null && (context.Expressions.Constant(set, out var values) /* KLD0041: Extract the standard CLR constant or nested closure field before validating its concrete scalar or bounded collection meaning at this translation site. */) && (IsSupportedCollection(values) /* KLD0041: Require the CLR constant to be an Array or a closed List<T>; reject other runtime collection types before enumeration. */))
        {
            return (Create(values!, candidate, parameter, context) /* KLD0041: Enumerate only the admitted CLR Array or List<T> constant and immediately translate each validated value into a typed IN operand. */);
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

    private static bool IsSupportedCollection(object? /* KLD0041: Admit only a runtime Array or List<T> supplied by CLR expression constants; validate each runtime element before creating a typed IN operand. */ values) => values is not null
        && (values is Array || (values.GetType() /* KLD0041: Require the CLR constant to be an Array or a closed List<T>; reject other runtime collection types before enumeration. */).IsGenericType && (values.GetType() /* KLD0041: Require the CLR constant to be an Array or a closed List<T>; reject other runtime collection types before enumeration. */).GetGenericTypeDefinition() == typeof(List<>));

    private static InPredicate Create(object /* KLD0041: Admit only a runtime Array or List<T> supplied by CLR expression constants; validate each runtime element before creating a typed IN operand. */ values, Expression candidate, ParameterExpression parameter, QueryTranslationContext context)
    {
        var operands = new List<Operand>();
        foreach (var item in ((IEnumerable)values /* KLD0042: The validated runtime constant is an Array or List<T> of a runtime element type; use its native CLR enumerator and immediately validate each scalar literal. */))
        {
            if (operands.Count == context.Limits.MaximumInItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.InListBudgetExceededMessage);
            }

            operands.Add((QueryExpressions.Literal(item) /* KLD0041: Convert this original CLR constant value to a bounded validated scalar JSON operand before it enters the typed query AST. */));
        }

        if (operands.Count == QueryPredicateTokens.EmptyCount)
        {
            throw QueryExpressions.Unsupported();
        }

        var value = context.Expressions.Operand(candidate, parameter);
        return new InPredicate(value, [.. operands], false);
    }
}
