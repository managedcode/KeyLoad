namespace KeyLoad.Features.QueryExecution;

/// <summary>Consumes only SQL trivia in allocation-free bounded chunks.</summary>
internal static class SqlTriviaReader
{
    internal const int MaximumChunkCharacters = 256;

    internal static SqlTriviaStatus Read(ReadOnlySpan<char> sql, ref int offset, ref SqlTriviaState state, int maximumDepth)
    {
        var end = offset + Math.Min(sql.Length - offset, MaximumChunkCharacters);
        while (offset < end)
        {
            if (state.InLineComment)
            {
                ReadLine(sql, ref offset, ref state);
                continue;
            }
            if (offset + 1 == end && end < sql.Length)
            { return SqlTriviaStatus.More; }
            if (state.BlockDepth > 0)
            {
                ReadBlock(sql, ref offset, ref state);
            }
            else if (!StartTrivia(sql, ref offset, ref state))
            {
                return SqlTriviaStatus.Complete;
            }
            if (state.BlockDepth > maximumDepth)
            { return SqlTriviaStatus.DepthLimitExceeded; }
        }
        return offset < sql.Length ? SqlTriviaStatus.More
            : state.BlockDepth > 0 ? SqlTriviaStatus.UnterminatedComment : SqlTriviaStatus.Complete;
    }

    private static void ReadLine(ReadOnlySpan<char> sql, ref int offset, ref SqlTriviaState state)
    {
        var value = sql[offset++];
        if (value is SqlTriviaSyntax.CarriageReturn or SqlTriviaSyntax.LineFeed)
        { state.InLineComment = false; }
    }

    private static void ReadBlock(ReadOnlySpan<char> sql, ref int offset, ref SqlTriviaState state)
    {
        if (IsPair(sql, offset, SqlTriviaSyntax.Slash, SqlTriviaSyntax.Star))
        { state.BlockDepth++; offset += SqlTriviaSyntax.PairCharacters; }
        else if (IsPair(sql, offset, SqlTriviaSyntax.Star, SqlTriviaSyntax.Slash))
        { state.BlockDepth--; offset += SqlTriviaSyntax.PairCharacters; }
        else
        { offset++; }
    }

    private static bool StartTrivia(ReadOnlySpan<char> sql, ref int offset, ref SqlTriviaState state)
    {
        if (char.IsWhiteSpace(sql[offset]))
        { offset++; return true; }
        var line = IsPair(sql, offset, SqlTriviaSyntax.Dash, SqlTriviaSyntax.Dash);
        var block = IsPair(sql, offset, SqlTriviaSyntax.Slash, SqlTriviaSyntax.Star);
        if (!line && !block)
        { return false; }
        state.InLineComment = line;
        state.BlockDepth = block ? 1 : 0;
        offset += SqlTriviaSyntax.PairCharacters;
        return true;
    }

    private static bool IsPair(ReadOnlySpan<char> sql, int offset, char first, char second)
        => offset + 1 < sql.Length && sql[offset] == first && sql[offset + 1] == second;
}
