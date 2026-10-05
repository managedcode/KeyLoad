using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Query;

namespace KeyLoad.Client;

/// <summary>An immutable Q1 expression adapter. Expressions are translated; application delegates are never executed.</summary>
public sealed class KeyLoadQuery<T>
{
    private readonly PartitionRef partition;
    private readonly SelectQuery query;
    internal KeyLoadQuery(PartitionRef partition, SelectQuery query) { this.partition = partition; this.query = query; }
    private KeyLoadQuery<T> With(SelectQuery value) => new(partition, value);
    /// <summary>Adds a supported expression as the query filter.</summary>
    /// <param name="expression">Predicate translated to the canonical Q1 expression tree.</param>
    /// <returns>A new builder retaining this builder's current query.</returns>
    public KeyLoadQuery<T> Where(Expression<Func<T, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var filter = QueryPredicateExpression.Filter(expression.Body, expression.Parameters[0]);
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
        var item = new Ordering(QueryExpressions.Field(field.Body, field.Parameters[0]), descending);
        return With(query with { Order = append ? [.. query.Order, item] : [item] });
    }
    /// <summary>Selects supported fields into the query projection.</summary>
    /// <typeparam name="TProjection">Application projection shape used for expression translation.</typeparam>
    /// <param name="expression">Supported projection expression.</param>
    /// <returns>A new builder with an immutable field projection.</returns>
    public KeyLoadQuery<T> Select<TProjection>(Expression<Func<T, TProjection>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return With(query with { Projection = QueryExpressions.Projection(expression.Body, expression.Parameters[0]) });
    }
    /// <summary>Sets the maximum result page size.</summary>
    /// <param name="limit">Positive maximum number of query rows.</param>
    /// <returns>A new builder with the requested limit.</returns>
    public KeyLoadQuery<T> Take(int limit)
    {
        if (limit < 1)
        {
            throw Errors.Fail(ErrorCode.Validation, "The query limit must be positive.");
        }

        return With(query with { Limit = limit });
    }
    /// <summary>Requests the supported query plan description.</summary>
    /// <returns>A new builder whose request includes an explain flag.</returns>
    public KeyLoadQuery<T> Explain() => With(query with { Explain = true });
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
    public static string DocumentId<T>(T document) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
    /// <summary>Projects the canonical document revision in a typed query.</summary>
    /// <typeparam name="T">Record type used by the expression.</typeparam>
    /// <param name="document">Expression parameter marker.</param>
    /// <returns>The document revision when translated by the query builder.</returns>
    public static long DocumentRevision<T>(T document) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
    /// <summary>Tests the canonical JSON null state in a typed query.</summary>
    /// <typeparam name="T">Value type used by the expression.</typeparam>
    /// <param name="value">Expression marker value.</param>
    /// <returns>A query result when translated by the query builder.</returns>
    public static bool IsNull<T>(T value) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
    /// <summary>Tests the canonical JSON missing-field state in a typed query.</summary>
    /// <typeparam name="T">Value type used by the expression.</typeparam>
    /// <param name="value">Expression marker value.</param>
    /// <returns>A query result when translated by the query builder.</returns>
    public static bool IsMissing<T>(T value) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
}

internal static class QueryExpressions
{
    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, QueryPredicateTokens.UnsupportedExpressionMessage);
    private static string Name(MemberInfo member) => member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonDefaults.Options.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;
    private static bool Integer(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    private static int Width(Type type) => type == typeof(byte) || type == typeof(sbyte) ? 8 : type == typeof(short) || type == typeof(ushort) ? 16
        : type == typeof(int) || type == typeof(uint) ? 32 : 64;
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
    public static string Field(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression is MethodCallExpression marker && marker.Method.DeclaringType == typeof(QueryFunctions)
            && marker.Arguments.Count == 1 && marker.Arguments[0] == parameter)
        {
            return marker.Method.Name switch { nameof(QueryFunctions.DocumentId) => "/@id", nameof(QueryFunctions.DocumentRevision) => "/@revision", _ => throw Unsupported() };
        }

        var parts = new List<string>();
        if (!WalkFieldPath(expression, parameter, 1, parts) || parts.Count == 0)
        {
            throw Unsupported();
        }

        return "/" + string.Join('/', parts.Select(part => part.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
    }

    private static bool WalkFieldPath(Expression node, ParameterExpression parameter, int depth, List<string> parts)
    {
        if (depth > QueryPredicateTokens.MaximumDepth)
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
                    && WalkFieldPath(member.Expression, parameter, depth + 1, parts);
            }

            if (member.Expression.Type == typeof(string) || member.Expression.Type.IsPrimitive || member.Expression.Type == typeof(decimal)
                || member.Member.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }
                || member.Member is not PropertyInfo && member.Member.GetCustomAttribute<JsonIncludeAttribute>() is null)
            {
                return false;
            }

            if (!WalkFieldPath(member.Expression, parameter, depth + 1, parts))
            {
                return false;
            }

            parts.Add(Name(member.Member));
            return true;
        }
        if (node is BinaryExpression { NodeType: ExpressionType.ArrayIndex } index
            && Constant(index.Right, out var value) && value is int ordinal && ordinal >= 0
            && WalkFieldPath(index.Left, parameter, depth + 1, parts))
        { parts.Add(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)); return true; }
        return false;
    }
    internal static bool Constant(Expression expression, out object? value, int depth = 1)
    {
        if (depth > QueryPredicateTokens.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantDepthExceededMessage);
        }

        expression = Unwrap(expression);
        if (expression is ConstantExpression constant)
        { value = constant.Value; return true; }
        if (expression is MemberExpression { Member: FieldInfo field, Expression: { } target } && Constant(target, out var instance, depth + 1))
        { value = field.GetValue(instance); return true; }
        if (expression is NewArrayExpression array && array.NodeType == ExpressionType.NewArrayInit)
        {
            if (array.Expressions.Count > QueryPredicateTokens.MaximumConstantArrayItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantArrayBudgetExceededMessage);
            }

            var items = new List<object?>();
            foreach (var item in array.Expressions)
            { if (!Constant(item, out var element, depth + 1)) { value = null; return false; } items.Add(element); }
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
    internal static Operand Operand(Expression expression, ParameterExpression parameter)
        => Constant(expression, out var value) ? Literal(value) : new FieldOperand(Field(expression, parameter));
    public static ImmutableArray<Selection> Projection(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression == parameter)
        {
            return [new("*", "*")];
        }

        if (expression is NewExpression { Members: { } members } creation)
        {
            return [.. creation.Arguments.Select((item, index) => new Selection(Field(item, parameter), Name(members[index])))];
        }

        if (expression is MemberInitExpression initialization)
        {
            if (initialization.NewExpression.Arguments.Count != 0)
            {
                throw Unsupported();
            }

            return [.. initialization.Bindings.Select(binding => binding is MemberAssignment assignment
                ? new Selection(Field(assignment.Expression, parameter), Name(binding.Member)) : throw Unsupported())];
        }
        var path = Field(expression, parameter);
        return [new(path, path == QueryPredicateTokens.IdentifierPath ? "id" : path == QueryPredicateTokens.RevisionPath ? "revision"
            : path[(path.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];
    }
}
