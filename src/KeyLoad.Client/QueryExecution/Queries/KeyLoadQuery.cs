using System.Collections.Immutable;
using System.Linq.Expressions;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>An immutable Q1 expression adapter. Expressions are translated; application delegates are never executed.</summary>
public sealed class KeyLoadQuery<T>
{
    private readonly PartitionRef partition;
    private readonly SelectQuery query;
    private readonly IOptions<QueryTranslationOptions> translationOptions;
    private readonly QueryTranslationContext context;
    internal KeyLoadQuery(PartitionRef partition, SelectQuery query, IOptions<QueryTranslationOptions> translationOptions)
    {
        translationOptions.Value.Validate();
        this.partition = partition;
        this.query = query;
        this.translationOptions = translationOptions;
        context = new(translationOptions);
    }
    private KeyLoadQuery<T> With(SelectQuery value) => new(partition, value, translationOptions);
    /// <summary>Adds a supported expression as the query filter.</summary>
    /// <param name="expression">Predicate translated to the canonical Q1 expression tree.</param>
    /// <returns>A new builder retaining this builder's current query.</returns>
    public KeyLoadQuery<T> Where(Expression<Func<T, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var filter = QueryPredicateExpression.Filter(expression.Body, expression.Parameters[QueryPredicateTokens.ParameterIndex], context);
        return With(query with
        {
            Filter = query.Filter is null ? filter : new Logical(query.Filter, QueryPredicateTokens.And, filter)
        });
    }
    /// <summary>Orders the query by one field in ascending order.</summary>
    /// <typeparam name="TValue">Field value type.</typeparam>
    /// <param name="field">Supported field expression.</param>
    /// <returns>A new builder with the requested ordering.</returns>
    public KeyLoadQuery<T> OrderBy<TValue>(Expression<Func<T, TValue>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Order(field, false, false);
    }
    /// <summary>Orders the query by one field in descending order.</summary>
    /// <typeparam name="TValue">Field value type.</typeparam>
    /// <param name="field">Supported field expression.</param>
    /// <returns>A new builder with the requested ordering.</returns>
    public KeyLoadQuery<T> OrderByDescending<TValue>(Expression<Func<T, TValue>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Order(field, true, false);
    }
    /// <summary>Adds an ascending tie-breaker after existing order fields.</summary>
    /// <typeparam name="TValue">Field value type.</typeparam>
    /// <param name="field">Supported field expression.</param>
    /// <returns>A new builder with the appended ordering.</returns>
    public KeyLoadQuery<T> ThenBy<TValue>(Expression<Func<T, TValue>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Order(field, false, true);
    }
    /// <summary>Adds a descending tie-breaker after existing order fields.</summary>
    /// <typeparam name="TValue">Field value type.</typeparam>
    /// <param name="field">Supported field expression.</param>
    /// <returns>A new builder with the appended ordering.</returns>
    public KeyLoadQuery<T> ThenByDescending<TValue>(Expression<Func<T, TValue>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Order(field, true, true);
    }
    private KeyLoadQuery<T> Order(LambdaExpression field, bool descending, bool append)
    {
        var item = new Ordering(context.Expressions.Field(field.Body, field.Parameters[QueryPredicateTokens.ParameterIndex]), descending);
        return With(query with
        {
            Order = append ? [.. query.Order, item] : [item]
        });
    }
    /// <summary>Selects supported fields into the query projection.</summary>
    /// <typeparam name="TProjection">Application projection shape used for expression translation.</typeparam>
    /// <param name="expression">Supported projection expression.</param>
    /// <returns>A new builder with an immutable field projection.</returns>
    public KeyLoadQuery<T> Select<TProjection>(Expression<Func<T, TProjection>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return With(query with
        {
            Projection = context.Expressions.Projection(expression.Body, expression.Parameters[QueryPredicateTokens.ParameterIndex])
        });
    }
    /// <summary>Sets the maximum result page size.</summary>
    /// <param name="limit">Positive maximum number of query rows.</param>
    /// <returns>A new builder with the requested limit.</returns>
    public KeyLoadQuery<T> Take(int limit)
    {
        if (limit < QueryPredicateTokens.MinimumQueryLimit)
        {
            throw Errors.Fail(ErrorCode.Validation, QueryPredicateTokens.PositiveLimitMessage);
        }

        return With(query with
        {
            Limit = limit
        });
    }
    /// <summary>Requests the supported query plan description.</summary>
    /// <returns>A new builder whose request includes an explain flag.</returns>
    public KeyLoadQuery<T> Explain() => With(query with { Explain = true });
    /// <summary>Attaches a bounded immutable vector and explicit graph scope to the canonical search request.</summary>
    /// <param name="field">Persisted vector member path; its getter is never invoked.</param>
    /// <param name="vector">Immutable named-vector equivalent, bounded by translation options.</param>
    /// <param name="space">Canonical vector profile validated by the server.</param>
    /// <param name="scope">Explicit same-partition graph scope validated by the server.</param>
    /// <returns>The existing typed graph-search request for the original SDK transport.</returns>
    public GraphSearchRequest AttachVector(Expression<Func<T, ImmutableArray<float>>> field,
        ImmutableArray<float> vector, VectorSpace space, GraphScope scope)
        => QueryVectorAttachment.Lower(partition, query, context, field, vector, space, scope);

    /// <summary>Creates the immutable canonical AST request for this builder.</summary>
    /// <param name="allowFullScan">Whether the caller permits a query without a selective predicate.</param>
    /// <param name="cursor">Optional continuation cursor for the query page.</param>
    /// <returns>A typed request retaining the query's immutable projection and ordering.</returns>
    public AstQueryRequest ToRequest(bool allowFullScan = false, string? cursor = null)
        => new(partition, query, AllowFullScan: allowFullScan, Cursor: cursor);
}
