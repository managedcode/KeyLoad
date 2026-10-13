using System.Buffers.Binary;
namespace KeyLoad.Server;

internal static class PostgresNumericDecimal
{
    internal static decimal Binary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < PostgresNumericProtocol.HeaderBytes)
        { throw Invalid(); }
        var count = BinaryPrimitives.ReadInt16BigEndian(bytes[PostgresNumericProtocol.CountOffset..]);
        var weight = BinaryPrimitives.ReadInt16BigEndian(bytes[PostgresNumericProtocol.WeightOffset..]);
        var sign = BinaryPrimitives.ReadUInt16BigEndian(bytes[PostgresNumericProtocol.SignOffset..]);
        var scale = BinaryPrimitives.ReadInt16BigEndian(bytes[PostgresNumericProtocol.ScaleOffset..]);
        if (count < PostgresNumericProtocol.ZeroCount || bytes.Length != PostgresNumericProtocol.HeaderBytes + count * sizeof(short)
            || scale < PostgresNumericProtocol.ZeroScale || scale > PostgresNumericProtocol.MaximumDisplayScale)
        { throw Invalid(); }
        if (sign is PostgresNumericProtocol.NaN or PostgresNumericProtocol.PositiveInfinity or PostgresNumericProtocol.NegativeInfinity)
        { throw Unsupported(); }
        if (sign is not (PostgresNumericProtocol.Positive or PostgresNumericProtocol.Negative))
        { throw Invalid(); }
        var first = PostgresNumericProtocol.AbsentDigit;
        var last = PostgresNumericProtocol.AbsentDigit;
        for (var index = PostgresNumericProtocol.FirstIndex; index < count; index++)
        {
            var digit = Digit(bytes, index);
            if (digit < PostgresNumericProtocol.ZeroDigit || digit >= PostgresNumericProtocol.Radix)
            { throw Invalid(); }
            if (digit == PostgresNumericProtocol.ZeroDigit)
            { continue; }
            if (first < PostgresNumericProtocol.FirstIndex)
            { first = index; }
            last = index;
        }
        if (first < PostgresNumericProtocol.FirstIndex)
        { return decimal.Zero; }
        if (last - first >= PostgresNumericProtocol.MaximumCoefficientGroups)
        { throw Unsupported(); }
        var coefficient = PostgresNumericProtocol.ZeroCoefficient;
        for (var index = first; index <= last; index++)
        { coefficient = coefficient * PostgresNumericProtocol.Radix + (uint)Digit(bytes, index); }
        var exponent = (long)PostgresNumericProtocol.RadixDigits * (weight - last);
        return Create(coefficient, exponent, sign == PostgresNumericProtocol.Negative, scale);
    }

    internal static decimal Text(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        { throw Invalid(); }
        var negative = text[PostgresNumericProtocol.FirstIndex] == PostgresNumericProtocol.Minus;
        var offset = text[PostgresNumericProtocol.FirstIndex] is PostgresNumericProtocol.Plus or PostgresNumericProtocol.Minus ? sizeof(byte) : PostgresNumericProtocol.FirstIndex;
        var unsigned = text[offset..];
        if (unsigned.Equals(PostgresNumericProtocol.NaNText, StringComparison.OrdinalIgnoreCase)
            || unsigned.Equals(PostgresNumericProtocol.InfinityText, StringComparison.OrdinalIgnoreCase)
            || unsigned.Equals(PostgresNumericProtocol.ShortInfinityText, StringComparison.OrdinalIgnoreCase))
        { throw Unsupported(); }
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
            { break; }
            if (value == PostgresNumericProtocol.Point)
            { if (point) { throw Invalid(); } point = true; continue; }
            if (value < PostgresNumericProtocol.Zero || value > PostgresNumericProtocol.Nine)
            { throw Invalid(); }
            if (value != PostgresNumericProtocol.Zero)
            {
                if (first < PostgresNumericProtocol.FirstIndex)
                { first = offset; firstDigit = digits; }
                last = offset;
                lastDigit = digits;
            }
            digits++;
            if (point)
            { fractional++; }
        }
        if (digits == PostgresNumericProtocol.ZeroCount)
        { throw Invalid(); }
        var explicitExponent = offset < text.Length ? ReadExponent(text[(offset + sizeof(byte))..]) : PostgresNumericProtocol.ZeroExponent;
        if (first < PostgresNumericProtocol.FirstIndex)
        { return decimal.Zero; }
        if (lastDigit - firstDigit >= PostgresNumericProtocol.MaximumCoefficientDigits)
        { throw Unsupported(); }
        var coefficient = PostgresNumericProtocol.ZeroCoefficient;
        for (var index = first; index <= last; index++)
        {
            if (text[index] == PostgresNumericProtocol.Point)
            { continue; }
            coefficient = coefficient * PostgresNumericProtocol.DecimalRadix + (uint)(text[index] - PostgresNumericProtocol.Zero);
        }
        var exponent = explicitExponent - fractional + digits - lastDigit - sizeof(byte);
        return Create(coefficient, exponent, negative);
    }

    private static long ReadExponent(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        { throw Invalid(); }
        var offset = text[PostgresNumericProtocol.FirstIndex] is PostgresNumericProtocol.Plus or PostgresNumericProtocol.Minus ? sizeof(byte) : PostgresNumericProtocol.FirstIndex;
        if (offset == text.Length)
        { throw Invalid(); }
        for (var index = offset; index < text.Length; index++)
        {
            if (text[index] < PostgresNumericProtocol.Zero || text[index] > PostgresNumericProtocol.Nine)
            { throw Invalid(); }
        }
        if (!long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var exponent))
        { throw Unsupported(); }
        if (exponent < int.MinValue || exponent > int.MaxValue)
        { throw Unsupported(); }
        return exponent;
    }

    private static decimal Create(UInt128 coefficient, long exponent, bool negative,
        int declaredScale = PostgresNumericProtocol.MaximumDecimalScale)
    {
        while (exponent < PostgresNumericProtocol.ZeroExponent && coefficient % PostgresNumericProtocol.DecimalRadix == PostgresNumericProtocol.ZeroCoefficient)
        { coefficient /= PostgresNumericProtocol.DecimalRadix; exponent++; }
        if (exponent < -Math.Min(declaredScale, PostgresNumericProtocol.MaximumDecimalScale)
            || exponent > PostgresNumericProtocol.MaximumDecimalScale)
        { throw Unsupported(); }
        while (exponent > PostgresNumericProtocol.ZeroExponent)
        {
            if (coefficient > PostgresNumericProtocol.MaximumCoefficient / PostgresNumericProtocol.DecimalRadix)
            { throw Unsupported(); }
            coefficient *= PostgresNumericProtocol.DecimalRadix;
            exponent--;
        }
        if (coefficient > PostgresNumericProtocol.MaximumCoefficient)
        { throw Unsupported(); }
        return new decimal(unchecked((int)(uint)coefficient), unchecked((int)(uint)(coefficient >> PostgresNumericProtocol.WordBits)),
            unchecked((int)(uint)(coefficient >> PostgresNumericProtocol.TwoWordBits)), negative, checked((byte)-exponent));
    }

    private static short Digit(ReadOnlySpan<byte> bytes, int index)
        => BinaryPrimitives.ReadInt16BigEndian(bytes[(PostgresNumericProtocol.HeaderBytes + index * sizeof(short))..]);

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
    private static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
}
