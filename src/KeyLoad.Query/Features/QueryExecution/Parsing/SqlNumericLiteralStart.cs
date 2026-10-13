namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlNumericLiteralStart
{
    private const char MinusCharacter = '-';
    private const char PlusCharacter = '+';
    private const char DecimalPoint = '.';
    private const int AdjacentElementOffset = 1;
    internal static bool Read(string sql, int index)
    {
        if (sql[index] is MinusCharacter or PlusCharacter)
        { index++; }
        if (index >= sql.Length)
        { return false; }
        return char.IsDigit(sql[index]) || sql[index] == DecimalPoint
            && index + AdjacentElementOffset < sql.Length
            && char.IsDigit(sql[index + AdjacentElementOffset]);
    }

}
