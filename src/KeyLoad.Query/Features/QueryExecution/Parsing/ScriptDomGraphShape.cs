using KeyLoad.Features.QueryExecution;
using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomGraphShape
{
    internal static IList<Dom.ScalarExpression> Read(string sql, IOptions<DatabaseLimits> limitsOptions,
        ScriptDomGraphContext context, string function)
    {
        var statement = ScriptDomSyntax.Parse(sql, limitsOptions, context.Budget.Check, RequireTerminalSemicolon, context.Budget.Cancellation);
        context.Check(statement, RootDepth);
        if (statement is not Dom.SelectStatement select || select.QueryExpression is not Dom.QuerySpecification query
            || select.WithCtesAndXmlNamespaces is not null || select.Into is not null || select.On is not null
            || select.ComputeClauses.Count != EmptyCount || select.OptimizerHints.Count != EmptyCount
            || query.UniqueRowFilter != default || query.TopRowFilter is not null || query.WhereClause is not null
            || query.GroupByClause is not null || query.HavingClause is not null || query.WindowClause is not null
            || query.ForClause is not null || query.OrderByClause is not null || query.OffsetClause is not null
            || query.SelectElements.Count != SingleElement
            || query.SelectElements[FirstIndex] is not Dom.SelectStarExpression { Qualifier: null }
            || query.FromClause is not { TableReferences.Count: SingleElement } from
            || from.TableReferences[FirstIndex] is not Dom.SchemaObjectFunctionTableReference table
            || table.Alias is not null || table.ForPath || table.Columns.Count != EmptyCount
            || table.SchemaObject.Identifiers.Count != SingleElement
            || !string.Equals(table.SchemaObject.Identifiers[FirstIndex].Value, function, StringComparison.OrdinalIgnoreCase))
        { throw ScriptDomErrors.Unsupported(); }
        context.Check(query, ChildDepth);
        context.Check(table, ChildDepth);
        return table.Parameters;
    }
    internal static IList<Dom.ScalarExpression> Call(Dom.ScalarExpression expression, string function,
        ScriptDomGraphContext context, int depth, int? arity = null)
    {
        context.Check(expression, depth);
        if (expression is not Dom.FunctionCall call || call.CallTarget is not null
            || !string.Equals(call.FunctionName.Value, function, StringComparison.OrdinalIgnoreCase)
            || call.UniqueRowFilter != default || call.OverClause is not null || call.WithinGroupClause is not null
            || call.IgnoreRespectNulls.Count != EmptyCount || call.AbsentOrNullOnNull.Count != EmptyCount
            || call.TrimOptions is not null || call.ReturnType is { Count: > EmptyCount } || call.WithArrayWrapper
            || call.JsonOrderByClause is not null || call.JsonParameters.Count != EmptyCount)
        { throw ScriptDomErrors.Unsupported(); }
        if (arity is { } count && call.Parameters.Count != count)
        { throw ScriptDomErrors.Invalid(); }
        return call.Parameters;
    }
    internal static bool IsNull(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        if (expression is not Dom.NullLiteral)
        { return false; }
        context.Check(expression, depth);
        return true;
    }
    private static void RequireTerminalSemicolon(IList<Dom.TSqlParserToken> tokens, Action check)
    {
        var terminators = EmptyCount;
        var nonTerminal = false;
        var batchSeparator = false;
        foreach (var token in tokens)
        {
            check();
            if (token.TokenType is Dom.TSqlTokenType.WhiteSpace or Dom.TSqlTokenType.SingleLineComment
                or Dom.TSqlTokenType.MultilineComment or Dom.TSqlTokenType.EndOfFile)
            { continue; }
            if (token.TokenType == Dom.TSqlTokenType.Go)
            { batchSeparator = true; }
            if (token.TokenType == Dom.TSqlTokenType.Semicolon)
            { terminators++; }
            else if (terminators != EmptyCount)
            { nonTerminal = true; }
        }
        if (!batchSeparator && !nonTerminal && terminators > SingleElement)
        { throw ScriptDomErrors.Invalid(); }
    }
    private const int EmptyCount = 0;
    private const int SingleElement = 1;
    private const int FirstIndex = 0;
    private const int RootDepth = 1;
    private const int ChildDepth = 2;
}
