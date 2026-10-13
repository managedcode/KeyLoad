using System.Globalization;
using System.Text;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.Server;

internal static class PostgresParameterNames
{
    private const char LiteralQuote = '\'';
    private const char IdentifierQuote = '"';
    private const char LineComment = '-';
    private const char BlockCommentOpen = '/';
    private const char BlockCommentMarker = '*';
    private const char PositionalParameter = '$';
    private const char NamedParameter = '@';
    internal static string Translate(string sql, int parameterCount, int maximumDepth, int cadence, CancellationToken token)
    {
        var output = new StringBuilder(sql.Length);
        var offset = PostgresProtocol.FirstField;
        while (offset < sql.Length)
        {
            token.ThrowIfCancellationRequested();
            var character = sql[offset];
            if (character is LiteralQuote or IdentifierQuote)
            { Quote(sql, ref offset, output, character, token); continue; }
            if (char.IsWhiteSpace(character) || Pair(sql, offset, LineComment, LineComment)
                || Pair(sql, offset, BlockCommentOpen, BlockCommentMarker))
            { Trivia(sql, ref offset, output, maximumDepth, cadence, token); continue; }
            if (character == PositionalParameter)
            { Parameter(sql, ref offset, output, parameterCount); continue; }
            if (character == NamedParameter)
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
            output.Append(character);
            offset++;
        }
        return output.ToString();
    }

    private static void Parameter(string sql, ref int offset, StringBuilder output, int count)
    {
        var start = ++offset;
        while (offset < sql.Length && char.IsAsciiDigit(sql[offset]))
        { offset++; }
        if (start == offset)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
        if (!int.TryParse(sql.AsSpan(start, offset - start), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= PostgresProtocol.AllRows || number > count)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
        output.Append(NamedParameter).Append(PostgresProtocol.ParameterPrefix).Append(number.ToString(CultureInfo.InvariantCulture));
    }

    private static void Quote(string sql, ref int offset, StringBuilder output, char quote, CancellationToken token)
    {
        output.Append(sql[offset++]);
        while (offset < sql.Length)
        {
            token.ThrowIfCancellationRequested();
            var character = sql[offset++];
            output.Append(character);
            if (character != quote)
            { continue; }
            if (offset < sql.Length && sql[offset] == quote)
            { output.Append(sql[offset++]); continue; }
            return;
        }
        throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
    }

    private static void Trivia(string sql, ref int offset, StringBuilder output, int maximumDepth,
        int cadence, CancellationToken token)
    {
        var start = offset;
        var state = default(SqlTriviaState);
        SqlTriviaStatus status;
        do
        {
            token.ThrowIfCancellationRequested();
            status = SqlTriviaReader.Read(sql.AsSpan(), ref offset, ref state, maximumDepth, cadence);
        }
        while (status == SqlTriviaStatus.More);
        if (status == SqlTriviaStatus.UnterminatedComment)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
        if (status == SqlTriviaStatus.DepthLimitExceeded)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PostgresProtocol.Invalid); }
        output.Append(sql, start, offset - start);
    }

    private static bool Pair(string sql, int offset, char left, char right)
        => offset + sizeof(byte) < sql.Length && sql[offset] == left && sql[offset + sizeof(byte)] == right;
}
