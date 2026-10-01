using System.Collections;
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
    private KeyLoadQuery(PartitionRef partition, SelectQuery query) { this.partition = partition; this.query = query; }
    public static KeyLoadQuery<T> From(PartitionRef partition, string collection)
        => new(partition, new(collection, null, [new("*", "*")], null, [], 100));
    private KeyLoadQuery<T> With(SelectQuery value) => new(partition, value);
    public KeyLoadQuery<T> Where(Expression<Func<T, bool>> expression)
    {
        var filter = QueryExpressions.Filter(expression.Body, expression.Parameters[0]);
        return With(query with { Filter = query.Filter is null ? filter : new Logical(query.Filter, "AND", filter) });
    }
    public KeyLoadQuery<T> OrderBy<TValue>(Expression<Func<T, TValue>> field) => Order(field, false, false);
    public KeyLoadQuery<T> OrderByDescending<TValue>(Expression<Func<T, TValue>> field) => Order(field, true, false);
    public KeyLoadQuery<T> ThenBy<TValue>(Expression<Func<T, TValue>> field) => Order(field, false, true);
    public KeyLoadQuery<T> ThenByDescending<TValue>(Expression<Func<T, TValue>> field) => Order(field, true, true);
    private KeyLoadQuery<T> Order(LambdaExpression field, bool descending, bool append)
    {
        var item = new Ordering(QueryExpressions.Field(field.Body, field.Parameters[0]), descending);
        return With(query with { Order = append ? [.. query.Order, item] : [item] });
    }
    public KeyLoadQuery<T> Select<TProjection>(Expression<Func<T, TProjection>> expression)
        => With(query with { Projection = QueryExpressions.Projection(expression.Body, expression.Parameters[0]) });
    public KeyLoadQuery<T> Take(int limit)
    {
        if (limit < 1) throw Errors.Fail(ErrorCode.Validation, "The query limit must be positive.");
        return With(query with { Limit = limit });
    }
    public KeyLoadQuery<T> Explain() => With(query with { Explain = true });
    public AstQueryRequest ToRequest(bool allowFullScan = false, string? cursor = null)
        => new(partition, query with { Projection = query.Projection.ToArray(), Order = query.Order.ToArray() }, AllowFullScan: allowFullScan, Cursor: cursor);
}

/// <summary>Query markers for canonical metadata and the distinction between JSON null and an absent field.</summary>
public static class QueryFunctions
{
    public static string DocumentId<T>(T document) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
    public static long DocumentRevision<T>(T document) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
    public static bool IsNull<T>(T value) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
    public static bool IsMissing<T>(T value) => throw new InvalidOperationException("This marker is only supported inside a KeyLoad query expression.");
}

internal static class QueryExpressions
{
    private static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, "This expression is outside the supported Q1 C# subset.");
    private static string Name(MemberInfo member) => member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonDefaults.Options.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;
    private static bool Integer(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    private static int Width(Type type) => type == typeof(byte) || type == typeof(sbyte) ? 8 : type == typeof(short) || type == typeof(ushort) ? 16
        : type == typeof(int) || type == typeof(uint) ? 32 : 64;
    private static bool Unsigned(Type type) => type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong);
    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            var from = convert.Operand.Type; var to = convert.Type;
            if (convert.Method is not null || !(to == typeof(object) || Nullable.GetUnderlyingType(to) == from
                || Integer(from) && to == typeof(decimal)
                || Integer(from) && Integer(to) && (Unsigned(from) == Unsigned(to) && Width(to) >= Width(from)
                    || Unsigned(from) && !Unsigned(to) && Width(to) > Width(from)))) throw Unsupported();
            expression = convert.Operand;
        }
        return expression;
    }
    public static string Field(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression is MethodCallExpression marker && marker.Method.DeclaringType == typeof(QueryFunctions)
            && marker.Arguments.Count == 1 && marker.Arguments[0] == parameter)
            return marker.Method.Name switch { nameof(QueryFunctions.DocumentId) => "/@id", nameof(QueryFunctions.DocumentRevision) => "/@revision", _ => throw Unsupported() };
        var parts = new List<string>();
        bool Walk(Expression node, int depth)
        {
            if (depth > 32) throw Errors.Fail(ErrorCode.BudgetExceeded, "The C# query path exceeds its depth budget.");
            node = Unwrap(node);
            if (node == parameter) return true;
            if (node is MemberExpression member && member.Expression is not null)
            {
                if (Nullable.GetUnderlyingType(member.Expression.Type) is not null)
                    return member.Member.Name == "Value" && Walk(member.Expression, depth + 1);
                if (member.Expression.Type == typeof(string) || member.Expression.Type.IsPrimitive || member.Expression.Type == typeof(decimal)
                    || member.Member.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }
                    || member.Member is not PropertyInfo && member.Member.GetCustomAttribute<JsonIncludeAttribute>() is null) return false;
                if (!Walk(member.Expression, depth + 1)) return false;
                parts.Add(Name(member.Member)); return true;
            }
            if (node is BinaryExpression { NodeType: ExpressionType.ArrayIndex } index && Constant(index.Right, out var value)
                && value is int ordinal && ordinal >= 0 && Walk(index.Left, depth + 1))
            { parts.Add(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)); return true; }
            return false;
        }
        if (!Walk(expression, 1) || parts.Count == 0) throw Unsupported();
        return "/" + string.Join('/', parts.Select(part => part.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
    }
    private static bool Constant(Expression expression, out object? value, int depth = 1)
    {
        if (depth > 32) throw Errors.Fail(ErrorCode.BudgetExceeded, "The C# constant expression exceeds its depth budget.");
        expression = Unwrap(expression);
        if (expression is ConstantExpression constant) { value = constant.Value; return true; }
        if (expression is MemberExpression { Member: FieldInfo field, Expression: { } target } && Constant(target, out var instance, depth + 1))
        { value = field.GetValue(instance); return true; }
        if (expression is NewArrayExpression array && array.NodeType == ExpressionType.NewArrayInit)
        {
            if (array.Expressions.Count > 256) throw Errors.Fail(ErrorCode.BudgetExceeded, "The C# constant array exceeds its item budget.");
            var items = new List<object?>();
            foreach (var item in array.Expressions) { if (!Constant(item, out var element, depth + 1)) { value = null; return false; } items.Add(element); }
            value = items.ToArray(); return true;
        }
        value = null; return false;
    }
    private static ValueOperand Literal(object? value)
    {
        if (value is not null && value is not (string or bool or decimal or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or JsonElement))
            throw Unsupported();
        if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d)) throw Unsupported();
        var element = value is JsonElement json ? json.Clone() : JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
        if (element.ValueKind is not (JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            || element.ValueKind == JsonValueKind.Number && !element.TryGetDecimal(out _)) throw Unsupported();
        return new(element);
    }
    private static Operand Operand(Expression expression, ParameterExpression parameter)
        => Constant(expression, out var value) ? Literal(value) : new FieldOperand(Field(expression, parameter));
    public static Predicate Filter(Expression expression, ParameterExpression parameter, int depth = 1)
    {
        if (depth > 32) throw Errors.Fail(ErrorCode.BudgetExceeded, "The C# query predicate exceeds its depth budget.");
        expression = Unwrap(expression);
        if (expression is BinaryExpression binary)
        {
            if (binary.Method is not null && binary.Method.DeclaringType != typeof(decimal) && binary.Method.DeclaringType != typeof(string)) throw Unsupported();
            if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
                return new Logical(Filter(binary.Left, parameter, depth + 1), binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR", Filter(binary.Right, parameter, depth + 1));
            var operation = binary.NodeType switch
            {
                ExpressionType.Equal => "=", ExpressionType.NotEqual => "!=", ExpressionType.GreaterThan => ">", ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<", ExpressionType.LessThanOrEqual => "<=", _ => throw Unsupported()
            };
            var left = Operand(binary.Left, parameter); var right = Operand(binary.Right, parameter);
            if (operation is "=" or "!=" && (left is ValueOperand { Value.ValueKind: JsonValueKind.Null } || right is ValueOperand { Value.ValueKind: JsonValueKind.Null }))
                return new NullTest(left is ValueOperand { Value.ValueKind: JsonValueKind.Null } ? right : left, operation == "!=", false);
            return new Comparison(left, operation, right);
        }
        if (expression is UnaryExpression { NodeType: ExpressionType.Not } negation) return new Negation(Filter(negation.Operand, parameter, depth + 1));
        if (expression is MethodCallExpression call)
        {
            if (call.Method.DeclaringType == typeof(QueryFunctions) && call.Arguments.Count == 1
                && call.Method.Name is nameof(QueryFunctions.IsNull) or nameof(QueryFunctions.IsMissing))
                return new NullTest(Operand(call.Arguments[0], parameter), false, call.Method.Name == nameof(QueryFunctions.IsMissing));
            Expression? set = null; Expression? candidate = null;
            if (call.Method.DeclaringType == typeof(Enumerable) && call.Method.Name == nameof(Enumerable.Contains) && call.Arguments.Count == 2)
            { set = call.Arguments[0]; candidate = call.Arguments[1]; }
            else if (call.Method.DeclaringType == typeof(MemoryExtensions) && call.Method.Name == nameof(MemoryExtensions.Contains) && call.Arguments.Count == 2
                && call.Arguments[0] is MethodCallExpression conversion && conversion.Method.Name == "op_Implicit"
                && conversion.Method.DeclaringType is { IsGenericType: true } span && span.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>)
                && conversion.Arguments.Count == 1 && conversion.Arguments[0].Type.IsArray)
            { set = conversion.Arguments[0]; candidate = call.Arguments[1]; }
            else if (call.Method.DeclaringType is { IsGenericType: true } list && list.GetGenericTypeDefinition() == typeof(List<>)
                && call.Method.Name == nameof(List<int>.Contains) && call.Arguments.Count == 1)
            { set = call.Object; candidate = call.Arguments[0]; }
            if (set is not null && candidate is not null && Constant(set, out var values) && values is not null
                && (values is Array || values.GetType().IsGenericType && values.GetType().GetGenericTypeDefinition() == typeof(List<>)))
            {
                var operands = new List<Operand>();
                foreach (var item in (IEnumerable)values)
                { if (operands.Count == 256) throw Errors.Fail(ErrorCode.BudgetExceeded, "The C# IN list exceeds its budget."); operands.Add(Literal(item)); }
                if (operands.Count == 0) throw Unsupported();
                return new InPredicate(Operand(candidate, parameter), operands.ToArray(), false);
            }
            throw Unsupported();
        }
        if (expression.Type == typeof(bool)) return new Comparison(Operand(expression, parameter), "=", Literal(true));
        throw Unsupported();
    }
    public static Selection[] Projection(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression == parameter) return [new("*", "*")];
        if (expression is NewExpression { Members: { } members } creation)
            return creation.Arguments.Select((item, index) => new Selection(Field(item, parameter), Name(members[index]))).ToArray();
        if (expression is MemberInitExpression initialization)
        {
            if (initialization.NewExpression.Arguments.Count != 0) throw Unsupported();
            return initialization.Bindings.Select(binding => binding is MemberAssignment assignment
                ? new Selection(Field(assignment.Expression, parameter), Name(binding.Member)) : throw Unsupported()).ToArray();
        }
        var path = Field(expression, parameter);
        return [new(path, path == "/@id" ? "id" : path == "/@revision" ? "revision"
            : path[(path.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];
    }
}
