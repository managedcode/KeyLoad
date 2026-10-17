namespace KeyLoad.Server;

internal static class PostgresNumericTextDecimal
{
    internal static decimal Parse(ReadOnlySpan<char> text)
    {
        var negative = Prefix(text, out var offset);
        var first = PostgresNumericProtocol.AbsentDigit;
        var last = PostgresNumericProtocol.AbsentDigit;
        var firstDigit = PostgresNumericProtocol.AbsentDigit;
        var lastDigit = PostgresNumericProtocol.AbsentDigit;
        var digits = PostgresNumericProtocol.ZeroCount;
        var fractional = PostgresNumericProtocol.ZeroCount;
        var point = false;
        for (; offset < text.Length; offset++)
        {
            var value = text[offset];
            if (value is PostgresNumericProtocol.Exponent or PostgresNumericProtocol.ExponentUpper)
            {
                break;
            }
            if (value == PostgresNumericProtocol.Point)
            {
                if (point)
                {
                    throw Invalid();
                }
                point = true;
                continue;
            }
            if (value < PostgresNumericProtocol.Zero || value > PostgresNumericProtocol.Nine)
            {
                throw Invalid();
            }
            if (value != PostgresNumericProtocol.Zero)
            {
                if (first < PostgresNumericProtocol.FirstIndex)
                {
                    first = offset;
                    firstDigit = digits;
                }
                last = offset;
                lastDigit = digits;
            }
            digits++;
            if (point)
            {
                fractional++;
            }
        }
        if (digits == PostgresNumericProtocol.ZeroCount)
        {
            throw Invalid();
        }
        var explicitExponent = offset < text.Length ? ReadExponent(text[(offset + sizeof(byte))..]) : PostgresNumericProtocol.ZeroExponent;
        if (first < PostgresNumericProtocol.FirstIndex)
        {
            return decimal.Zero;
        }
        if (lastDigit - firstDigit >= PostgresNumericProtocol.MaximumCoefficientDigits)
        {
            throw Unsupported();
        }
        var coefficient = Coefficient(text, first, last);
        var exponent = explicitExponent - fractional + digits - lastDigit - sizeof(byte);
        return PostgresNumericDecimal.Create(coefficient, exponent, negative);
    }

    private static long ReadExponent(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            throw Invalid();
        }
        var offset = text[PostgresNumericProtocol.FirstIndex] is PostgresNumericProtocol.Plus or PostgresNumericProtocol.Minus ? sizeof(byte) : PostgresNumericProtocol.FirstIndex;
        if (offset == text.Length)
        {
            throw Invalid();
        }
        for (var index = offset; index < text.Length; index++)
        {
            if (text[index] < PostgresNumericProtocol.Zero || text[index] > PostgresNumericProtocol.Nine)
            {
                throw Invalid();
            }
        }
        if (!long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var exponent))
        {
            throw Unsupported();
        }
        if (exponent < int.MinValue || exponent > int.MaxValue)
        {
            throw Unsupported();
        }
        return exponent;
    }

    private static bool Prefix(ReadOnlySpan<char> text, out int offset)
    {
        if (text.IsEmpty)
        {
            throw Invalid();
        }
        var negative = text[PostgresNumericProtocol.FirstIndex] == PostgresNumericProtocol.Minus;
        offset = text[PostgresNumericProtocol.FirstIndex] is PostgresNumericProtocol.Plus or PostgresNumericProtocol.Minus ? sizeof(byte) : PostgresNumericProtocol.FirstIndex;
        var unsigned = text[offset..];
        if (unsigned.Equals(PostgresNumericProtocol.NaNText, StringComparison.OrdinalIgnoreCase)
            || unsigned.Equals(PostgresNumericProtocol.InfinityText, StringComparison.OrdinalIgnoreCase)
            || unsigned.Equals(PostgresNumericProtocol.ShortInfinityText, StringComparison.OrdinalIgnoreCase))
        {
            throw Unsupported();
        }
        return negative;
    }

    private static UInt128 Coefficient(ReadOnlySpan<char> text, int first, int last)
    {
        var coefficient = PostgresNumericProtocol.ZeroCoefficient;
        for (var index = first; index <= last; index++)
        {
            if (text[index] == PostgresNumericProtocol.Point)
            {
                continue;
            }
            coefficient = coefficient * PostgresNumericProtocol.DecimalRadix + (uint)(text[index] - PostgresNumericProtocol.Zero);
        }
        return coefficient;
    }

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
    private static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
}
