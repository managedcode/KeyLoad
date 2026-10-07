using System.Collections.Immutable;
using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

/// <summary>Lowers one bounded vector attachment with the existing non-evaluating field translator.</summary>
internal static class QueryVectorAttachment
{
    private const int Version = 1;
    private const int AllFieldsCount = 1;
    private const int FirstSelection = 0;
    private const string AttachmentLimitDetail = "The query vector attachment exceeds its item budget.";
    private const string InvalidVectorDetail = "A finite vector and a matching typed vector space are required.";

    internal static GraphSearchRequest Lower<T>(PartitionRef partition, SelectQuery query,
        QueryTranslationContext context, Expression<Func<T, ImmutableArray<float>>> field,
        ImmutableArray<float> vector, VectorSpace space, GraphScope scope)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(scope);
        if (query.Filter is not null || !query.Order.IsEmpty || query.Explain
            || query.Projection.Length != AllFieldsCount
            || query.Projection[FirstSelection].Path != QueryPredicateTokens.Wildcard
            || query.Projection[FirstSelection].Alias != QueryPredicateTokens.Wildcard)
        { throw QueryExpressions.Unsupported(); }
        if (vector.IsDefaultOrEmpty)
        { throw Errors.Fail(ErrorCode.Validation, InvalidVectorDetail); }
        if (vector.Length > context.Limits.MaximumConstantArrayItems)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, AttachmentLimitDetail); }
        var path = context.Expressions.Field(field.Body, field.Parameters[QueryPredicateTokens.ParameterIndex]);
        return new(Version, new(partition, query.Collection, VectorField: path, Vector: vector,
            Space: space, Limit: query.Limit), Scope: scope);
    }
}
