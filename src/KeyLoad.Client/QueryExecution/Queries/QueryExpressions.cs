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
            && (Constant(index.Right, out var value) /* KLD0041: Extract the standard CLR constant or nested closure field before validating its concrete scalar or bounded collection meaning at this translation site. */) && (value is int ordinal /* KLD0042: The standard expression tree exposes a boxed constant; extract the array ordinal as int before enforcing its nonnegative bound. */) && ordinal >= QueryPredicateTokens.MinimumArrayOrdinal
            && WalkFieldPath(index.Left, parameter, depth + QueryPredicateTokens.DepthIncrement, parts))
        {
            parts.Add(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        return false;
    }
    internal bool Constant(Expression expression, out object? /* KLD0041: The standard ConstantExpression.Value and closure FieldInfo.GetValue APIs return object, including arbitrary closure targets required for nested field extraction. */ value, int depth = QueryPredicateTokens.FirstDepth)
    {
        if (depth > limits.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantDepthExceededMessage);
        }

        expression = Unwrap(expression);
        if (expression is ConstantExpression constant)
        {
            value = (constant.Value /* KLD0041: Read the original CLR expression constant value through its native object-valued API before validating its supported scalar or collection shape. */);
            return true;
        }
        if (expression is MemberExpression { Member: FieldInfo field, Expression: { } target } && (Constant(target, out var instance, depth + QueryPredicateTokens.DepthIncrement) /* KLD0041: Extract the standard CLR constant or nested closure field before validating its concrete scalar or bounded collection meaning at this translation site. */))
        {
            value = (field.GetValue(instance) /* KLD0041: Read only the admitted closure field through the native CLR reflection API; the runtime closure target and returned constant have no source-level generic type. */);
            return true;
        }
        if (expression is NewArrayExpression array && array.NodeType == ExpressionType.NewArrayInit)
        {
            if (array.Expressions.Count > limits.MaximumConstantArrayItems)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.ConstantArrayBudgetExceededMessage);
            }

            var /* KLD0041: Expression constants already arrive as CLR object values; retain them in one bounded exact-size array for IN translation without a List or another copy. */ items = (new object?[array.Expressions.Count] /* KLD0041: Store already-boxed CLR expression constants in this bounded exact-size array, preserving their values without a List allocation or a second copy. */);
            for (var index = 0; index < items.Length; index++)
            {
                if (!(Constant(array.Expressions[index], out var element, depth + QueryPredicateTokens.DepthIncrement) /* KLD0041: Extract the standard CLR constant or nested closure field before validating its concrete scalar or bounded collection meaning at this translation site. */))
                {
                    value = null;
                    return false;
                }
                items[index] = element;
            }
            value = items;
            return true;
        }
        value = null;
        return false;
    }
    internal static ValueOperand Literal(object? /* KLD0041: The standard ConstantExpression.Value and closure FieldInfo.GetValue APIs return object, including arbitrary closure targets required for nested field extraction. */ value)
    {
        if (value is not null && value is not (string or bool or decimal or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or JsonElement))
        {
            throw Unsupported();
        }

        if ((value is float f /* KLD0042: The standard expression tree exposes a boxed constant; extract float only to reject a non-finite literal before JSON serialization. */) && !float.IsFinite(f) || (value is double d /* KLD0042: The standard expression tree exposes a boxed constant; extract double only to reject a non-finite literal before JSON serialization. */) && !double.IsFinite(d))
        {
            throw Unsupported();
        }

        var element = (value is JsonElement json /* KLD0042: The standard expression tree exposes constants through object; extract its JsonElement value so the literal can clone and validate owned scalar JSON. */) ? json.Clone() : (JsonSerializer.SerializeToElement(value, JsonDefaults.Options) /* KLD0041: The CLR constant has only a runtime scalar type; serialize that already-boxed value after the preceding closed-type and finite-number checks. */);
        if (element.ValueKind is not (JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            || element.ValueKind == JsonValueKind.Number && !element.TryGetDecimal(out _))
        {
            throw Unsupported();
        }

        return new(element);
    }
    internal Operand Operand(Expression expression, ParameterExpression parameter)
        => (Constant(expression, out var value) /* KLD0041: Extract the standard CLR constant or nested closure field before validating its concrete scalar or bounded collection meaning at this translation site. */) ? (Literal(value) /* KLD0041: Convert this original CLR constant value to a bounded validated scalar JSON operand before it enters the typed query AST. */) : new FieldOperand(Field(expression, parameter));
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
