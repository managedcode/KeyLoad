using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

internal sealed class QueryExpressions(IOptions<QueryTranslationOptions> options)
{
    private const string ImplicitOperator = "op_Implicit";
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
            if (convert.Method is not null && !IntegerDecimalConversion(convert, from, to) || !(to == typeof(object) || Nullable.GetUnderlyingType(to) == from
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
    private static bool IntegerDecimalConversion(UnaryExpression conversion, Type from, Type to)
        => Integer(from) && to == typeof(decimal) && conversion.Method is { IsStatic: true } method
           && method.DeclaringType == typeof(decimal) && method.Name == ImplicitOperator
           && method.ReturnType == typeof(decimal)
           && method.GetParameters() is [{ ParameterType: var input }] && input == from;

    public string Field(Expression expression, ParameterExpression parameter)
    {
        expression = Unwrap(expression);
        if (expression is MethodCallExpression marker && marker.Method.DeclaringType == typeof(QueryFunctions)
                                                      && marker.Arguments.Count == QueryPredicateTokens.MarkerArgumentCount && marker.Arguments[QueryPredicateTokens.ParameterIndex] == parameter)
        {
            return marker.Method.Name switch
            {
                nameof(QueryFunctions.DocumentId) => QueryPredicateTokens.IdentifierPath,
                nameof(QueryFunctions.DocumentRevision) => QueryPredicateTokens.RevisionPath,
                _ => throw Unsupported()
            };
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
                || typeof(System.Collections.IEnumerable).IsAssignableFrom(member.Expression.Type)
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
        {
            parts.Add(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
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
        {
            value = constant.Value;
            return true;
        }
        if (expression is MemberExpression { Member: FieldInfo field, Expression: { } target } && Constant(target, out var instance, depth + QueryPredicateTokens.DepthIncrement))
        {
            value = field.GetValue(instance);
            return true;
        }
        if (expression is NewArrayExpression array && array.NodeType == ExpressionType.NewArrayInit)
        {
            if (array.Expressions.Count > limits.MaximumConstantArrayItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantArrayBudgetExceededMessage);
            }

            var items = new List<object?>();
            foreach (var item in array.Expressions)
            {
                if (!Constant(item, out var element, depth + QueryPredicateTokens.DepthIncrement))
                {
                    value = null;
                    return false;
                }
                items.Add(element);
            }
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
