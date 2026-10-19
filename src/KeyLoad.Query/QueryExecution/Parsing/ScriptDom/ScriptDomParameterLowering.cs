using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomParameterLowering
{
    internal static string Name(Dom.ScalarExpression expression, ScriptDomLoweringContext context)
    {
        if (expression is Dom.VariableReference variable)
        {
            if (context.PositionalParameterCount.HasValue)
            {
                throw ScriptDomErrors.Invalid();
            }
            var namedParameter = variable.Name.TrimStart(ParameterPrefix);
            global::KeyLoad.Core.JsonData.Identifier(namedParameter);
            return namedParameter;
        }
        if (expression is not Dom.MoneyLiteral literal || context.PositionalParameterCount is not { } count)
        {
            throw ScriptDomErrors.Unsupported();
        }
        if (!ScriptDomSyntax.TryGetSessionSlot(literal, count, out var canonicalName))
        {
            throw ScriptDomErrors.Invalid();
        }
        return canonicalName;
    }
    internal static int Count(Dom.ScalarExpression expression, ScriptDomLoweringContext context)
    {
        var name = Name(expression, context);
        if (context.Parameters is null || !context.Parameters.TryGetValue(name, out var value)
            || value.ValueKind != global::System.Text.Json.JsonValueKind.Number || !value.TryGetInt32(out var count))
        {
            throw ScriptDomErrors.Invalid();
        }
        return count;
    }
    private const char ParameterPrefix = '@';
}
