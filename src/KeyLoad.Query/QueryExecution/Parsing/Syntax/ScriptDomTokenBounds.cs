using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.QueryExecution;

internal static class ScriptDomTokenBounds
{
    internal static void Require(IList<Dom.TSqlParserToken> tokens, Action check)
        => Require(tokens, check, null);

    internal static void Require(IList<Dom.TSqlParserToken> tokens, Action check,
        Action<IList<Dom.TSqlParserToken>, Action>? validateTokens)
    {
        var terminators = EmptyCount;
        var unsupportedStructure = false;
        foreach (var token in tokens)
        {
            check();
            if (token.TokenType == Dom.TSqlTokenType.Go
                || token.TokenType == Dom.TSqlTokenType.Semicolon && ++terminators > SingleTerminator)
            {
                unsupportedStructure = true;
            }
        }
        validateTokens?.Invoke(tokens, check);
        if (unsupportedStructure)
        {
            throw ScriptDomErrors.Unsupported();
        }
    }

    private const int EmptyCount = 0;
    private const int SingleTerminator = 1;
}
