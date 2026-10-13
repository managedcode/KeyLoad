namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlUnicodeEscapeClause
{
    private const string Keyword = "UESCAPE";
    private const char DefaultEscape = '\\';
    private const char Quote = '\'';
    private const char IdentifierSeparator = '_';
    private const char Plus = '+';
    private const char IdentifierQuote = '"';
    private const char Nul = '\0';
    private const int First = 0;
    private const int SingleCharacter = 1;
    private const int AsciiMaximum = 127;

    internal static char Read(string sql, ref int index, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int interval, ref int sourceTokenCount, int maximum, int maximumDepth)
    {
        Trivia(sql, ref index, budget, interval, maximumDepth);
        if (!sql.AsSpan(index).StartsWith(Keyword, StringComparison.OrdinalIgnoreCase)
            || index + Keyword.Length < sql.Length && (char.IsLetterOrDigit(sql[index + Keyword.Length])
                || sql[index + Keyword.Length] == IdentifierSeparator))
        { return DefaultEscape; }
        index += Keyword.Length;
        SqlTokenizer.ChargeContinuation(ref sourceTokenCount, maximum);
        Trivia(sql, ref index, budget, interval, maximumDepth);
        SqlTokenizer.ChargeContinuation(ref sourceTokenCount, maximum);
        if (index >= sql.Length)
        { throw SqlSyntax.Invalid(); }
        string value;
        if (SqlEscapeStringReader.Starts(sql, index))
        { value = SqlEscapeStringReader.Read(sql, ref index, budget, interval, ref sourceTokenCount, maximum).Text; }
        else if (index < sql.Length && sql[index] == global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.Delimiter)
        { value = SqlDollarStringReader.Read(sql, ref index, budget, interval).Text; }
        else
        { value = SqlUnicodeStringReader.Quoted(sql, ref index, budget, interval, ref sourceTokenCount, maximum); }
        if (value.Length != SingleCharacter || value[First] > AsciiMaximum || value[First] == Nul
            || char.IsAsciiHexDigit(value[First]) || value[First] is Plus or Quote or IdentifierQuote
            || char.IsWhiteSpace(value[First]))
        { throw SqlSyntax.Invalid(); }
        return value[First];
    }

    private static void Trivia(string sql, ref int index, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int interval, int maximumDepth)
    {
        var state = default(global::KeyLoad.Features.QueryExecution.SqlTriviaState);
        global::KeyLoad.Features.QueryExecution.SqlTriviaStatus status;
        do
        {
            budget?.Check();
            status = global::KeyLoad.Features.QueryExecution.SqlTriviaReader.Read(sql.AsSpan(), ref index,
                ref state, maximumDepth, interval);
        } while (status == global::KeyLoad.Features.QueryExecution.SqlTriviaStatus.More);
        if (status == global::KeyLoad.Features.QueryExecution.SqlTriviaStatus.UnterminatedComment)
        { throw Errors.Fail(ErrorCode.Validation, global::KeyLoad.Query.Features.QueryExecution.SqlCommentSyntax.UnterminatedBlockDetail); }
        if (status == global::KeyLoad.Features.QueryExecution.SqlTriviaStatus.DepthLimitExceeded)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, global::KeyLoad.Query.Features.QueryExecution.SqlCommentSyntax.BlockDepthDetail); }
    }
}
