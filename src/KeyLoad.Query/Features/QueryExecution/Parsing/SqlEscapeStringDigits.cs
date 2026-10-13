using System.Text;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlEscapeStringDigits
{
    internal const int ByteDigits = 2;
    private const int UnicodeDigits = 4;
    private const int WideDigits = 8;
    private const int OctalDigits = 3;
    private const int Zero = 0;
    private const int One = 1;
    private const int HexRadix = 16;
    private const int OctalRadix = 8;
    private const int LetterOffset = 10;
    private const uint HighFirst = 0xd800;
    private const uint HighLast = 0xdbff;
    private const char DigitFirst = '0';
    private const char DigitLast = '9';
    private const char LowerFirst = 'a';
    private const char OctalLast = '7';
    private const char Slash = '\\';
    private const char UnicodeMarker = 'u';
    private const char WideUnicodeMarker = 'U';
    private const int PrefixWidth = 2;

    internal static uint Hex(string sql, ref int index, int count, bool exact,
        global::KeyLoad.Core.ReadExecutionBudget? budget, int interval)
    {
        var start = index;
        uint result = Zero;
        while (index < sql.Length && index - start < count && char.IsAsciiHexDigit(sql[index]))
        {
            SqlEscapeStringReader.Check(budget, index, interval);
            var current = sql[index++];
            var digit = current is >= DigitFirst and <= DigitLast ? current - DigitFirst
                : char.ToLowerInvariant(current) - LowerFirst + LetterOffset;
            result = result * HexRadix + (uint)digit;
        }
        if (index == start || exact && index - start != count)
        { throw SqlSyntax.Invalid(); }
        return result;
    }

    internal static uint Octal(string sql, ref int index, char first, global::KeyLoad.Core.ReadExecutionBudget? budget, int interval)
    {
        var result = (uint)(first - DigitFirst);
        for (var count = One; count < OctalDigits && index < sql.Length
            && sql[index] is >= DigitFirst and <= OctalLast; count++)
        {
            SqlEscapeStringReader.Check(budget, index, interval);
            result = result * OctalRadix + (uint)(sql[index++] - DigitFirst);
        }
        return result;
    }

    internal static Rune Unicode(string sql, ref int index, bool wide, global::KeyLoad.Core.ReadExecutionBudget? budget, int interval)
    {
        var value = Hex(sql, ref index, wide ? WideDigits : UnicodeDigits, exact: true, budget, interval);
        if (value is >= HighFirst and <= HighLast)
        {
            if (index + One >= sql.Length || sql[index] != Slash
                || sql[index + One] is not (UnicodeMarker or WideUnicodeMarker))
            { throw SqlSyntax.Invalid(); }
            var lowWide = sql[index + One] == WideUnicodeMarker;
            index += PrefixWidth;
            var low = Hex(sql, ref index, lowWide ? WideDigits : UnicodeDigits, exact: true, budget, interval);
            if (low > char.MaxValue || !Rune.TryCreate((char)value, (char)low, out var pair))
            { throw SqlSyntax.Invalid(); }
            return pair;
        }
        if (value > int.MaxValue || !Rune.TryCreate((int)value, out var rune))
        { throw SqlSyntax.Invalid(); }
        return rune;
    }
}
