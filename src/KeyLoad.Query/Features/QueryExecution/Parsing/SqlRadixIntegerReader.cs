using System.Globalization;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlRadixIntegerReader
{
    private const int First = 0;
    private const int Adjacent = 1;
    private const int PrefixWidth = 2;
    private const char Zero = '0';
    private const char One = '1';
    private const char Seven = '7';
    private const char Hex = 'x';
    private const char Binary = 'b';
    private const char Octal = 'o';
    private const char Plus = '+';
    private const char Minus = '-';
    private const uint OctalRadix = 8;

    internal static bool Read(string sql, int start, ref int index,
        global::KeyLoad.Core.ReadExecutionBudget? budget, int cadence)
    {
        var digits = sql[start] is Minus or Plus ? start + Adjacent : start;
        if (digits + Adjacent >= sql.Length || sql[digits] != Zero)
        { return false; }
        var kind = char.ToLowerInvariant(sql[digits + Adjacent]);
        if (kind is not (Hex or Binary or Octal))
        { return false; }
        index = digits + PrefixWidth;
        while (index < sql.Length && Digit(sql[index], kind))
        {
            index++;
            if (index % cadence == First)
            { budget?.Check(); }
        }
        return true;
    }

    internal static bool Parse(string text, out decimal number)
    {
        number = default;
        var value = text.AsSpan();
        if (value.IsEmpty)
        { return false; }
        var negative = value[First] == Minus;
        if (value[First] is Minus or Plus)
        { value = value[Adjacent..]; }
        if (value.Length < PrefixWidth || value[First] != Zero)
        { return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number); }
        var kind = char.ToLowerInvariant(value[Adjacent]);
        if (kind is not (Hex or Binary or Octal))
        { return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number); }
        var digits = value[PrefixWidth..];
        global::System.UInt128 integer;
        var parsed = kind == Octal ? OctalNumber(digits, out integer)
            : global::System.UInt128.TryParse(digits,
                kind == Hex ? NumberStyles.AllowHexSpecifier : NumberStyles.AllowBinarySpecifier,
                CultureInfo.InvariantCulture, out integer);
        if (!parsed || integer > (global::System.UInt128)decimal.MaxValue)
        { return false; }
        number = (decimal)integer;
        if (negative)
        { number = -number; }
        return true;
    }

    private static bool OctalNumber(ReadOnlySpan<char> digits, out global::System.UInt128 number)
    {
        number = default;
        if (digits.IsEmpty)
        { return false; }
        var maximum = (global::System.UInt128)decimal.MaxValue;
        foreach (var value in digits)
        {
            if (value is < Zero or > Seven)
            { return false; }
            var digit = (uint)(value - Zero);
            if (number > (maximum - digit) / OctalRadix)
            { return false; }
            number = number * OctalRadix + digit;
        }
        return true;
    }

    private static bool Digit(char value, char kind) => kind switch
    {
        Hex => char.IsAsciiHexDigit(value),
        Binary => value is Zero or One,
        Octal => value is >= Zero and <= Seven,
        _ => false
    };
}
