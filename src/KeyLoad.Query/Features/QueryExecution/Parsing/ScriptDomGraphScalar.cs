using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomGraphScalar
{
    internal static string String(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        context.Check(expression, depth);
        if (expression is Dom.StringLiteral literal)
        { return literal.Value; }
        if (expression is Dom.VariableReference variable)
        {
            var value = context.Parameter(variable);
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text)
            { return text; }
            throw Errors.Fail(ErrorCode.Validation, context.ParameterDetail);
        }
        throw ScriptDomErrors.Unsupported();
    }
    internal static int Integer(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        context.Check(expression, depth);
        if (expression is Dom.VariableReference variable)
        {
            var value = context.Parameter(variable);
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            { return number; }
            throw Errors.Fail(ErrorCode.Validation, context.ParameterDetail);
        }
        if (expression is Dom.IntegerLiteral literal
            && int.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
        { return integer; }
        if (expression is Dom.UnaryExpression
            {
                UnaryExpressionType: Dom.UnaryExpressionType.Negative,
                Expression: Dom.IntegerLiteral signed
            } unary)
        {
            context.Check(unary.Expression, depth + ChildDepth);
            if (long.TryParse(signed.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var absolute)
                && absolute <= NegativeMinimumMagnitude)
            { return checked((int)-absolute); }
        }
        throw ScriptDomErrors.Invalid();
    }
    internal static double Weight(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        context.Check(expression, depth);
        double value;
        if (expression is Dom.VariableReference variable)
        {
            var parameter = context.Parameter(variable);
            if (parameter.ValueKind != JsonValueKind.Number || !parameter.TryGetDouble(out value))
            { throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.ParameterDetail); }
        }
        else if (expression is Dom.Literal literal && (literal is Dom.IntegerLiteral or Dom.NumericLiteral or Dom.RealLiteral)
            && double.TryParse(literal.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        { value = number; }
        else
        { throw ScriptDomErrors.Invalid(); }
        if (!double.IsFinite(value) || value < MinimumWeight)
        { throw ScriptDomErrors.Invalid(); }
        return value;
    }
    internal static ImmutableArray<string>? Strings(Dom.ScalarExpression expression, string function,
        ScriptDomGraphContext context, int depth)
    {
        if (ScriptDomGraphShape.IsNull(expression, context, depth))
        { return null; }
        var arguments = ScriptDomGraphShape.Call(expression, function, context, depth);
        var values = ImmutableArray.CreateBuilder<string>(arguments.Count);
        foreach (var argument in arguments)
        { values.Add(String(argument, context, depth + ChildDepth)); }
        return values.ToImmutable();
    }
    private const int ChildDepth = 1;
    private const long NegativeMinimumMagnitude = 2_147_483_648;
    private const double MinimumWeight = 0;
}
