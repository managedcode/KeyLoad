using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Features.QueryExecution;

internal static class ScriptDomTokenBounds
{
    internal static void Require(IList<Dom.TSqlParserToken> tokens, DatabaseLimits limits, Action check)
    {
        var meaningful = EmptyCount;
        var depth = EmptyCount;
        foreach (var token in tokens)
        {
            check();
            if (token.TokenType == Dom.TSqlTokenType.MultilineComment)
            { RequireCommentDepth(token.Text, limits.MaxQueryDepth, check); }
            if (token.TokenType is Dom.TSqlTokenType.WhiteSpace or Dom.TSqlTokenType.SingleLineComment
                or Dom.TSqlTokenType.MultilineComment or Dom.TSqlTokenType.EndOfFile)
            { continue; }
            if (++meaningful > limits.MaxQueryTokens)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.TokensDetail); }
            if (token.Text == OpenParenthesis && ++depth > limits.MaxQueryDepth)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.DepthDetail); }
            if (token.Text == CloseParenthesis)
            { depth--; }
        }
    }

    private static void RequireCommentDepth(string text, int maximumDepth, Action check)
    {
        var depth = EmptyCount;
        for (var index = FirstIndex; index < text.Length - NextCharacter; index++)
        {
            check();
            if (text[index] == Slash && text[index + NextCharacter] == Asterisk)
            {
                if (++depth > maximumDepth)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.DepthDetail); }
                index++;
            }
            else if (text[index] == Asterisk && text[index + NextCharacter] == Slash)
            { depth--; index++; }
        }
    }
    private const string OpenParenthesis = "(";
    private const string CloseParenthesis = ")";
    private const char Slash = '/';
    private const char Asterisk = '*';
    private const int FirstIndex = 0;
    private const int NextCharacter = 1;

    private const int EmptyCount = 0;
}
