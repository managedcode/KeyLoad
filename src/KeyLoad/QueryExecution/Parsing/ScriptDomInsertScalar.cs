using System.Text.Json;
using KeyLoad.Core;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomInsertScalar
{
    internal static void Write(Utf8JsonWriter writer, Dom.ScalarExpression value,
        ScriptDomCompositionParameters parameters, int depth, Action checkBudget, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        checkBudget();
        token.ThrowIfCancellationRequested();
        ExecutionStackGuard.Ensure();
        if (value is Dom.PrimaryExpression { Collation: not null })
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        switch (value)
        {
            case Dom.StringLiteral text:
                writer.WriteStringValue(text.Value);
                break;
            case Dom.NullLiteral:
                writer.WriteNullValue();
                break;
            case Dom.IntegerLiteral or Dom.NumericLiteral or Dom.RealLiteral:
                Number(writer, ((Dom.Literal)value).Value);
                break;
            case Dom.UnaryExpression unary:
                Unary(writer, unary);
                break;
            case Dom.VariableReference or Dom.MoneyLiteral:
                Parameter(writer, parameters.ReadScalar(value));
                break;
            case Dom.ParenthesisExpression grouped:
                Write(writer, grouped.Expression, parameters, depth + 1, checkBudget, token);
                break;
            default:
                throw ScriptDomOperationErrors.Unsupported();
        }
    }
    private static void Parameter(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True
            or JsonValueKind.False or JsonValueKind.Null))
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        value.WriteTo(writer);
    }
    private static void Unary(Utf8JsonWriter writer, Dom.UnaryExpression unary)
    {
        if (unary.Expression is Dom.PrimaryExpression { Collation: not null })
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (unary.Expression is not (Dom.IntegerLiteral or Dom.NumericLiteral or Dom.RealLiteral)
            || unary.Expression is not Dom.Literal number
            || unary.UnaryExpressionType is not (Dom.UnaryExpressionType.Positive or Dom.UnaryExpressionType.Negative))
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        Number(writer, (unary.UnaryExpressionType == Dom.UnaryExpressionType.Negative ? Negative : string.Empty) + number.Value);
    }
    private static void Number(Utf8JsonWriter writer, string value)
    {
        var text = value.StartsWith(Dot, StringComparison.Ordinal) ? Zero + value
            : value.StartsWith(NegativeDot, StringComparison.Ordinal) ? NegativeZero + value[Negative.Length..] : value;
        text = text.EndsWith(Dot, StringComparison.Ordinal) ? text + Zero : text;
        text = text.Replace(DotExponent, ZeroDotExponent, StringComparison.Ordinal)
            .Replace(DotUpperExponent, ZeroDotUpperExponent, StringComparison.Ordinal);
        var prefix = text.StartsWith(Negative, StringComparison.Ordinal) ? Negative.Length : Empty;
        var significant = prefix;
        while (significant + 1 < text.Length && text[significant] == ZeroDigit
            && char.IsAsciiDigit(text[significant + 1]))
        {
            significant++;
        }
        if (significant > prefix)
        {
            text = prefix == Empty ? text[significant..] : Negative + text[significant..];
        }
        try
        {
            writer.WriteRawValue(text, skipInputValidation: false);
        }
        catch (JsonException) { throw ScriptDomOperationErrors.Invalid(); }
    }
    private const int Empty = 0;
    private const char ZeroDigit = '0';
    private const string DotExponent = ".e";
    private const string ZeroDotExponent = ".0e";
    private const string DotUpperExponent = ".E";
    private const string ZeroDotUpperExponent = ".0E";
    private const string Dot = ".";
    private const string NegativeDot = "-.";
    private const string Negative = "-";
    private const string Zero = "0";
    private const string NegativeZero = "-0";
}
