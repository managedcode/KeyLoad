namespace KeyLoad.Features.QueryExecution;

internal static class SqlStringContinuationReader
{
    private const int Adjacent = 1;
    private const char Quote = '\'';
    private const char Dash = '-';
    private const char Space = ' ';
    private const char Tab = '\t';
    private const char FormFeed = '\f';
    private const char VerticalTab = '\v';
    private const char LineFeed = '\n';
    private const char CarriageReturn = '\r';

    internal static bool? Read(ReadOnlySpan<char> sql, ref int offset,
        ref SqlStringContinuationState state, int maximumChunkCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChunkCharacters);
        var end = offset + Math.Min(sql.Length - offset, maximumChunkCharacters);
        while (offset < end)
        {
            var value = sql[offset];
            if (state.InLineComment)
            { Line(value, ref offset, ref state); continue; }
            if (value == Quote)
            { return state.HasNewline; }
            if (value is LineFeed or CarriageReturn)
            { state.HasNewline = true; offset++; continue; }
            if (value is Space or Tab or FormFeed or VerticalTab)
            { offset++; continue; }
            if (value != Dash || offset + Adjacent >= sql.Length || sql[offset + Adjacent] != Dash)
            { return false; }
            state.InLineComment = true;
            offset += Adjacent + Adjacent;
        }
        return offset < sql.Length ? null : false;
    }

    private static void Line(char value, ref int offset, ref SqlStringContinuationState state)
    {
        offset++;
        if (value is LineFeed or CarriageReturn)
        { state.InLineComment = false; state.HasNewline = true; }
    }
}
