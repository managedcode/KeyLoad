using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        return With(query with { Filter = query.Filter is null ? filter : new Logical(query.Filter, QueryPredicateTokens.And, filter) });
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
        return With(query with { Order = append ? [.. query.Order, item] : [item] });
    }
    /// <summary>Selects supported fields into the query projection.</summary>
    /// <typeparam name="TProjection">Application projection shape used for expression translation.</typeparam>
    /// <param name="expression">Supported projection expression.</param>
    /// <returns>A new builder with an immutable field projection.</returns>
    public KeyLoadQuery<T> Select<TProjection>(Expression<Func<T, TProjection>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return With(query with { Projection = context.Expressions.Projection(expression.Body, expression.Parameters[QueryPredicateTokens.ParameterIndex]) });
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

        return With(query with { Limit = limit });
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

/// <summary>Query markers for canonical metadata and the distinction between JSON null and an absent field.</summary>
public static class QueryFunctions
{
    /// <summary>Projects the canonical entity identifier in a typed query.</summary>
    /// <typeparam name="T">Record type used by the expression.</typeparam>
    /// <param name="document">Expression parameter marker.</param>
    /// <returns>The entity identifier when translated by the query builder.</returns>
    public static string DocumentId<T>(T document) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
    /// <summary>Projects the canonical document revision in a typed query.</summary>
    /// <typeparam name="T">Record type used by the expression.</typeparam>
    /// <param name="document">Expression parameter marker.</param>
    /// <returns>The document revision when translated by the query builder.</returns>
    public static long DocumentRevision<T>(T document) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
    /// <summary>Tests the canonical JSON null state in a typed query.</summary>
    /// <typeparam name="T">Value type used by the expression.</typeparam>
    /// <param name="value">Expression marker value.</param>
    /// <returns>A query result when translated by the query builder.</returns>
    public static bool IsNull<T>(T value) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
    /// <summary>Tests the canonical JSON missing-field state in a typed query.</summary>
    /// <typeparam name="T">Value type used by the expression.</typeparam>
    /// <param name="value">Expression marker value.</param>
    /// <returns>A query result when translated by the query builder.</returns>
    public static bool IsMissing<T>(T value) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
}

internal sealed class QueryExpressions(IOptions<QueryTranslationOptions> options)
{
    private readonly QueryTranslationOptions limits = options.Value;
    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, QueryPredicateTokens.UnsupportedExpressionMessage);
    private static string Name(MemberInfo member) => member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonDefaults.Options.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;
    private static bool Integer(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    private static int Width(Type type) => type == typeof(byte) || type == typeof(sbyte) ? QueryPredicateTokens.ByteWidth : type == typeof(short) || type == typeof(ushort) ? QueryPredicateTokens.ShortWidth
        : type == typeof(int) || type == typeof(uint) ? QueryPredicateTokens.IntWidth : QueryPredicateTokens.LongWidth;
    private static bool Unsigned(Type type) => type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong);
    internal static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            var from = convert.Operand.Type;
            var to = convert.Type;
            if (convert.Method is not null || !(to == typeof(object) || Nullable.GetUnderlyingType(to) == from
                || Integer(from) && to == typeof(decimal)
                || Integer(from) && Integer(to) && (Unsigned(from) == Unsigned(to) && Width(to) >= Width(from)
                    || Unsigned(from) && !Unsigned(to) && Width(to) > Width(from))))
            {
                throw Unsupported();
            }

            expression = convert.Operand;
        }
        return expression;
    }
    public string Field(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression is MethodCallExpression marker && marker.Method.DeclaringType == typeof(QueryFunctions)
            && marker.Arguments.Count == QueryPredicateTokens.MarkerArgumentCount && marker.Arguments[QueryPredicateTokens.ParameterIndex] == parameter)
        {
            return marker.Method.Name switch { nameof(QueryFunctions.DocumentId) => QueryPredicateTokens.IdentifierPath, nameof(QueryFunctions.DocumentRevision) => QueryPredicateTokens.RevisionPath, _ => throw Unsupported() };
        }

        var parts = new List<string>();
        if (!WalkFieldPath(expression, parameter, QueryPredicateTokens.FirstDepth, parts) || parts.Count == QueryPredicateTokens.EmptyCount)
        {
            throw Unsupported();
        }

        return QueryPredicateTokens.PathSeparator + string.Join(QueryPredicateTokens.PathSeparatorCharacter, parts.Select(part => part.Replace(QueryPredicateTokens.EscapeMarker, QueryPredicateTokens.EscapedMarker, StringComparison.Ordinal).Replace(QueryPredicateTokens.PathSeparator, QueryPredicateTokens.EscapedSeparator, StringComparison.Ordinal)));
    }

    private bool WalkFieldPath(Expression node, ParameterExpression parameter, int depth, List<string> parts)
    {
        if (depth > limits.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.QueryPathDepthExceededMessage);
        }

        node = Unwrap(node);
        if (node == parameter)
        {
            return true;
        }

        if (node is MemberExpression member && member.Expression is not null)
        {
            if (Nullable.GetUnderlyingType(member.Expression.Type) is not null)
            {
                return member.Member.Name == QueryPredicateTokens.NullableValueMemberName
                    && WalkFieldPath(member.Expression, parameter, depth + QueryPredicateTokens.DepthIncrement, parts);
            }

            if (member.Expression.Type == typeof(string) || member.Expression.Type.IsPrimitive || member.Expression.Type == typeof(decimal)
                || member.Member.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }
                || member.Member is not PropertyInfo && member.Member.GetCustomAttribute<JsonIncludeAttribute>() is null)
            {
                return false;
            }

            if (!WalkFieldPath(member.Expression, parameter, depth + QueryPredicateTokens.DepthIncrement, parts))
            {
                return false;
            }

            parts.Add(Name(member.Member));
            return true;
        }
        if (node is BinaryExpression { NodeType: ExpressionType.ArrayIndex } index
            && Constant(index.Right, out var value) && value is int ordinal && ordinal >= QueryPredicateTokens.MinimumArrayOrdinal
            && WalkFieldPath(index.Left, parameter, depth + QueryPredicateTokens.DepthIncrement, parts))
        { parts.Add(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)); return true; }
        return false;
    }
    internal bool Constant(Expression expression, out object? value, int depth = QueryPredicateTokens.FirstDepth)
    {
        if (depth > limits.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantDepthExceededMessage);
        }

        expression = Unwrap(expression);
        if (expression is ConstantExpression constant)
        { value = constant.Value; return true; }
        if (expression is MemberExpression { Member: FieldInfo field, Expression: { } target } && Constant(target, out var instance, depth + QueryPredicateTokens.DepthIncrement))
        { value = field.GetValue(instance); return true; }
        if (expression is NewArrayExpression array && array.NodeType == ExpressionType.NewArrayInit)
        {
            if (array.Expressions.Count > limits.MaximumConstantArrayItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantArrayBudgetExceededMessage);
            }

            var items = new List<object?>();
            foreach (var item in array.Expressions)
            { if (!Constant(item, out var element, depth + QueryPredicateTokens.DepthIncrement)) { value = null; return false; } items.Add(element); }
            value = items.ToArray();
            return true;
        }
        value = null;
        return false;
    }
    internal static ValueOperand Literal(object? value)
    {
        if (value is not null && value is not (string or bool or decimal or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or JsonElement))
        {
            throw Unsupported();
        }

        if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d))
        {
            throw Unsupported();
        }

        var element = value is JsonElement json ? json.Clone() : JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
        if (element.ValueKind is not (JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            || element.ValueKind == JsonValueKind.Number && !element.TryGetDecimal(out _))
        {
            throw Unsupported();
        }

        return new(element);
    }
    internal Operand Operand(Expression expression, ParameterExpression parameter)
        => Constant(expression, out var value) ? Literal(value) : new FieldOperand(Field(expression, parameter));
    public ImmutableArray<Selection> Projection(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression == parameter)
        {
            return [new(QueryPredicateTokens.Wildcard, QueryPredicateTokens.Wildcard)];
        }

        if (expression is NewExpression { Members: { } members } creation)
        {
            return [.. creation.Arguments.Select((item, index) => new Selection(Field(item, parameter), Name(members[index])))];
        }

        if (expression is MemberInitExpression initialization)
        {
            if (initialization.NewExpression.Arguments.Count != QueryPredicateTokens.EmptyCount)
            {
                throw Unsupported();
            }

            return [.. initialization.Bindings.Select(binding => binding is MemberAssignment assignment
                ? new Selection(Field(assignment.Expression, parameter), Name(binding.Member)) : throw Unsupported())];
        }
        var path = Field(expression, parameter);
        return [new(path, path == QueryPredicateTokens.IdentifierPath ? QueryPredicateTokens.IdentifierName : path == QueryPredicateTokens.RevisionPath ? QueryPredicateTokens.RevisionName
            : path[(path.LastIndexOf(QueryPredicateTokens.PathSeparatorCharacter) + QueryPredicateTokens.DepthIncrement)..].Replace(QueryPredicateTokens.EscapedSeparator, QueryPredicateTokens.PathSeparator, StringComparison.Ordinal).Replace(QueryPredicateTokens.EscapedMarker, QueryPredicateTokens.EscapeMarker, StringComparison.Ordinal))];
    }
}
