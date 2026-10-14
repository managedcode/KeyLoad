using System.Globalization;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomSourceLowering
{
    internal static (string Collection, string? Alias, ModelQuerySource? Model) Read(Dom.TableReference table, ScriptDomLoweringContext context)
    {
        context.Observe(RootDepth);
        if (table is Dom.TableReferenceWithAlias { ForPath: true })
        { throw ScriptDomErrors.Unsupported(); }
        if (table is Dom.NamedTableReference named)
        {
            if (named.TableHints.Count != EmptyCount || named.TableSampleClause is not null || named.TemporalClause is not null)
            { throw ScriptDomErrors.Unsupported(); }
            return (ScriptDomNames.Object(named.SchemaObject), Alias(named.Alias), null);
        }
        if (table is not Dom.SchemaObjectFunctionTableReference function)
        { throw ScriptDomErrors.Unsupported(); }
        if (function.Columns.Count != EmptyCount)
        { throw ScriptDomErrors.Unsupported(); }
        var name = ScriptDomNames.Object(function.SchemaObject);
        var values = function.Parameters;
        var alias = Alias(function.Alias);
        if (StringComparer.OrdinalIgnoreCase.Equals(name, SqlSyntax.Events) && values.Count is StreamArguments or StreamGenerationArguments)
        {
            var collection = Identifier(values[FirstIndex], context);
            return (collection, alias, new(ModelQuerySourceKind.Events, Identifier(values[SecondIndex], context), Generation(values, StreamArguments, context)));
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(name, SqlSyntax.QueueMessages) && values.Count == SingleElement)
        {
            var queue = Identifier(values[FirstIndex], context);
            return (queue, alias, new(ModelQuerySourceKind.QueueMessages, queue));
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(name, SqlSyntax.TopicEvents) && values.Count is TopicArguments or TopicGenerationArguments)
        {
            var topic = Identifier(values[FirstIndex], context);
            return (topic, alias, new(ModelQuerySourceKind.TopicEvents, topic, Generation(values, TopicArguments, context)));
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(name, SqlSyntax.Events)
            || StringComparer.OrdinalIgnoreCase.Equals(name, SqlSyntax.QueueMessages)
            || StringComparer.OrdinalIgnoreCase.Equals(name, SqlSyntax.TopicEvents))
        { throw ScriptDomErrors.Invalid(); }
        throw ScriptDomErrors.Unsupported();
    }
    private static string? Alias(Dom.Identifier? alias)
    {
        if (alias is null)
        { return null; }
        global::KeyLoad.Core.JsonData.Identifier(alias.Value);
        return alias.Value;
    }
    private static string Identifier(Dom.ScalarExpression expression, ScriptDomLoweringContext context)
    {
        context.Observe(RootDepth);
        var value = expression is Dom.StringLiteral text ? text.Value : StringParameter(expression, context);
        global::KeyLoad.Core.JsonData.Identifier(value);
        return value;
    }
    private static string StringParameter(Dom.ScalarExpression expression, ScriptDomLoweringContext context)
    {
        if (expression is not (Dom.VariableReference or Dom.MoneyLiteral))
        { throw ScriptDomErrors.Invalid(); }
        var name = ScriptDomParameterLowering.Name(expression, context);
        if (context.Parameters is null || !context.Parameters.TryGetValue(name, out var value)
            || value.ValueKind != global::System.Text.Json.JsonValueKind.String || value.GetString() is not { } text)
        { throw ScriptDomErrors.Invalid(); }
        return text;
    }
    private static long Generation(IList<Dom.ScalarExpression> values, int index, ScriptDomLoweringContext context)
    {
        if (values.Count == index)
        { return FirstGeneration; }
        context.Observe(RootDepth);
        long generation;
        if (values[index] is Dom.UnaryExpression unary && unary.Expression is Dom.IntegerLiteral signed)
        {
            if (unary.UnaryExpressionType is not (Dom.UnaryExpressionType.Positive or Dom.UnaryExpressionType.Negative))
            { throw ScriptDomErrors.Unsupported(); }
            var text = (unary.UnaryExpressionType == Dom.UnaryExpressionType.Negative ? Negative : Positive) + signed.Value;
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out generation))
            { throw ScriptDomErrors.Invalid(); }
        }
        else if (values[index] is Dom.IntegerLiteral value)
        {
            if (!long.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out generation))
            { throw ScriptDomErrors.Invalid(); }
        }
        else
        {
            var name = ScriptDomParameterLowering.Name(values[index], context);
            if (context.Parameters is null || !context.Parameters.TryGetValue(name, out var parameter)
                || parameter.ValueKind != global::System.Text.Json.JsonValueKind.Number || !parameter.TryGetInt64(out generation))
            { throw ScriptDomErrors.Invalid(); }
        }
        if (generation < FirstGeneration)
        { throw ScriptDomErrors.Invalid(); }
        return generation;
    }
    private const long FirstGeneration = 1;

    private const int FirstIndex = 0;
    private const int SingleElement = 1;
    private const int EmptyCount = 0;
    private const int StreamArguments = 2;
    private const int StreamGenerationArguments = 3;
    private const int TopicArguments = 1;
    private const int TopicGenerationArguments = 2;
    private const int SecondIndex = 1;
    private const int RootDepth = 1;
    private const string Negative = "-";
    private const string Positive = "+";
}
