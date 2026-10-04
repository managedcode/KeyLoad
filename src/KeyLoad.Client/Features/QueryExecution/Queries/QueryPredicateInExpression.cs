using System.Collections;
using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateInExpression
{
    internal static Predicate Translate(Expression set, Expression candidate, ParameterExpression parameter)
    {
        if (set is not null && candidate is not null && QueryExpressions.Constant(set, out var values) && IsSupportedCollection(values))
        {
            return Create(values!, candidate, parameter);
        }

        throw QueryExpressions.Unsupported();
    }

    internal static bool TryGetCollection(MethodCallExpression call, out Expression set, out Expression candidate)
    {
        if (IsEnumerableContains(call))
        {
            set = call.Arguments[0];
            candidate = call.Arguments[1];
            return true;
        }

        if (IsSpanContains(call))
        {
            var conversion = (MethodCallExpression)call.Arguments[0];
            set = conversion.Arguments[0];
            candidate = call.Arguments[1];
            return true;
        }

        if (IsListContains(call))
        {
            set = call.Object!;
            candidate = call.Arguments[0];
            return true;
        }

        set = null!;
        candidate = null!;
        return false;
    }

    private static bool IsEnumerableContains(MethodCallExpression call) => call.Method.DeclaringType == typeof(Enumerable)
        && call.Method.Name == nameof(Enumerable.Contains)
        && call.Arguments.Count == 2;

    private static bool IsSpanContains(MethodCallExpression call)
    {
        if (call.Method.DeclaringType != typeof(MemoryExtensions) || call.Method.Name != nameof(MemoryExtensions.Contains)
            || call.Arguments.Count != 2 || call.Arguments[0] is not MethodCallExpression conversion)
        {
            return false;
        }

        return conversion.Method.Name == QueryPredicateTokens.ImplicitConversionOperatorName
            && conversion.Method.DeclaringType is { IsGenericType: true } span
            && span.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>)
            && conversion.Arguments.Count == 1
            && conversion.Arguments[0].Type.IsArray;
    }

    private static bool IsListContains(MethodCallExpression call) => call.Method.DeclaringType is { IsGenericType: true } list
        && list.GetGenericTypeDefinition() == typeof(List<>)
        && call.Method.Name == nameof(List<int>.Contains)
        && call.Arguments.Count == 1;

    private static bool IsSupportedCollection(object? values) => values is not null
        && (values is Array || values.GetType().IsGenericType && values.GetType().GetGenericTypeDefinition() == typeof(List<>));

    private static InPredicate Create(object values, Expression candidate, ParameterExpression parameter)
    {
        var operands = new List<Operand>();
        foreach (var item in (IEnumerable)values)
        {
            if (operands.Count == QueryPredicateTokens.MaximumInItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.InListBudgetExceededMessage);
            }

            operands.Add(QueryExpressions.Literal(item));
        }

        if (operands.Count == 0)
        {
            throw QueryExpressions.Unsupported();
        }

        var value = QueryExpressions.Operand(candidate, parameter);
        return new InPredicate(value, [.. operands], false);
    }
}
