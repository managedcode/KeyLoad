using System.Text;
using KeyLoad.Features.QueryExecution;
using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Client;

internal static class NativeCancelledSqlInspection
{
    private const int EmptyPrefixLength = 0;
    private const int NoParserErrors = 0;

    internal static bool MayWrite(string sql, int prefixLimit, IOptions<DatabaseLimits> limitsOptions)
    {
        var limits = limitsOptions.Value;
        limits.Validate();
        if (prefixLimit <= EmptyPrefixLength || sql.Length > limits.MaxQueryBytes)
        {
            return true;
        }
        var length = Math.Min(sql.Length, prefixLimit);
        var prefix = sql[..length];
        if (Encoding.UTF8.GetByteCount(prefix) > limits.MaxQueryBytes)
        {
            return true;
        }
        try
        {
            var parser = new Dom.TSql180Parser(initialQuotedIdentifiers: true, Dom.SqlEngineType.Standalone);
            using var reader = new StringReader(prefix);
            var tokens = parser.GetTokenStream(reader, out var errors);
            if (errors.Count != NoParserErrors)
            {
                return true;
            }
            ScriptDomTokenBounds.Require(tokens, limits, static () => { });
            return FirstMayWrite(tokens, length, sql.Length);
        }
        catch (KeyLoadException)
        {
            return true;
        }
    }

    private static bool FirstMayWrite(IList<Dom.TSqlParserToken> tokens, int prefixLength, int sourceLength)
    {
        foreach (var token in tokens)
        {
            if (token.TokenType is Dom.TSqlTokenType.WhiteSpace or Dom.TSqlTokenType.SingleLineComment
                or Dom.TSqlTokenType.MultilineComment)
            {
                continue;
            }
            if (token.TokenType != Dom.TSqlTokenType.Select)
            {
                return true;
            }
            var end = checked(token.Offset + token.Text.Length);
            // EOF of a truncated prefix is not the original keyword boundary.
            return end >= prefixLength && end != sourceLength;
        }
        return true;
    }
}
