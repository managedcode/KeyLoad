namespace KeyLoad.Features.QueryExecution;

internal static class SqlDollarQuoteReader
{
    internal const char Delimiter = '$';
    private const char IdentifierSeparator = '_';
    private const char NullCharacter = '\0';
    private const int NextCharacter = 1;
    private const int EmptyLength = 0;

    internal static bool CanStart(ReadOnlySpan<char> sql, int index)
    {
        if (sql[index] != Delimiter || index + NextCharacter >= sql.Length)
        { return false; }
        if (index > EmptyLength && (char.IsLetterOrDigit(sql[index - NextCharacter])
            || sql[index - NextCharacter] is IdentifierSeparator or Delimiter))
        { return false; }
        var next = sql[index + NextCharacter];
        return next is Delimiter or IdentifierSeparator || char.IsLetter(next);
    }

    internal static SqlDollarQuoteState Start(int index)
        => new() { Start = index, Offset = index + NextCharacter };

    internal static SqlDollarQuoteStatus Read(ReadOnlySpan<char> sql,
        ref SqlDollarQuoteState state, int cadence)
    {
        if (state.DelimiterLength == EmptyLength)
        { return Header(sql, ref state, cadence); }
        var end = state.Offset + Math.Min(cadence, sql.Length - state.Offset);
        var delimiter = sql.Slice(state.Start, state.DelimiterLength);
        while (state.Offset < end)
        {
            if (sql[state.Offset] == NullCharacter)
            { return SqlDollarQuoteStatus.Invalid; }
            if (sql[state.Offset] == Delimiter && sql[state.Offset..].StartsWith(delimiter,
                StringComparison.Ordinal))
            {
                state.ContentEnd = state.Offset;
                state.Offset += state.DelimiterLength;
                return SqlDollarQuoteStatus.Complete;
            }
            state.Offset++;
        }
        return state.Offset == sql.Length ? SqlDollarQuoteStatus.Invalid : SqlDollarQuoteStatus.More;
    }

    private static SqlDollarQuoteStatus Header(ReadOnlySpan<char> sql,
        ref SqlDollarQuoteState state, int cadence)
    {
        var end = state.Offset + Math.Min(cadence, sql.Length - state.Offset);
        while (state.Offset < end)
        {
            var character = sql[state.Offset++];
            if (character == Delimiter)
            {
                state.DelimiterLength = state.Offset - state.Start;
                state.ContentStart = state.Offset;
                return SqlDollarQuoteStatus.More;
            }
            if (character != IdentifierSeparator && !char.IsLetterOrDigit(character))
            { return SqlDollarQuoteStatus.Invalid; }
        }
        return state.Offset == sql.Length ? SqlDollarQuoteStatus.Invalid : SqlDollarQuoteStatus.More;
    }
}
