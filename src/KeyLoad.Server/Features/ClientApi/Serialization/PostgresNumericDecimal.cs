using System.Buffers.Binary;
namespace KeyLoad.Server;

internal static class PostgresNumericDecimal
{
    internal static decimal Binary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < PostgresNumericProtocol.HeaderBytes)
        {
            throw Invalid();
        }
        var count = BinaryPrimitives.ReadInt16BigEndian(bytes[PostgresNumericProtocol.CountOffset..]);
        var weight = BinaryPrimitives.ReadInt16BigEndian(bytes[PostgresNumericProtocol.WeightOffset..]);
        var sign = BinaryPrimitives.ReadUInt16BigEndian(bytes[PostgresNumericProtocol.SignOffset..]);
        var scale = BinaryPrimitives.ReadInt16BigEndian(bytes[PostgresNumericProtocol.ScaleOffset..]);
        if (count < PostgresNumericProtocol.ZeroCount || bytes.Length != PostgresNumericProtocol.HeaderBytes + count * sizeof(short)
            || scale < PostgresNumericProtocol.ZeroScale || scale > PostgresNumericProtocol.MaximumDisplayScale)
        {
            throw Invalid();
        }
        if (sign is PostgresNumericProtocol.NaN or PostgresNumericProtocol.PositiveInfinity or PostgresNumericProtocol.NegativeInfinity)
        {
            throw Unsupported();
        }
        if (sign is not (PostgresNumericProtocol.Positive or PostgresNumericProtocol.Negative))
        {
            throw Invalid();
        }
        var first = PostgresNumericProtocol.AbsentDigit;
        var last = PostgresNumericProtocol.AbsentDigit;
        for (var index = PostgresNumericProtocol.FirstIndex; index < count; index++)
        {
            var digit = Digit(bytes, index);
            if (digit < PostgresNumericProtocol.ZeroDigit || digit >= PostgresNumericProtocol.Radix)
            {
                throw Invalid();
            }
            if (digit == PostgresNumericProtocol.ZeroDigit)
            {
                continue;
            }
            if (first < PostgresNumericProtocol.FirstIndex)
            {
                first = index;
            }
            last = index;
        }
        if (first < PostgresNumericProtocol.FirstIndex)
        {
            return decimal.Zero;
        }
        if (last - first >= PostgresNumericProtocol.MaximumCoefficientGroups)
        {
            throw Unsupported();
        }
        var coefficient = PostgresNumericProtocol.ZeroCoefficient;
        for (var index = first; index <= last; index++)
        {
            coefficient = coefficient * PostgresNumericProtocol.Radix + (uint)Digit(bytes, index);
        }
        var exponent = (long)PostgresNumericProtocol.RadixDigits * (weight - last);
        return Create(coefficient, exponent, sign == PostgresNumericProtocol.Negative, scale);
    }

    internal static decimal Text(ReadOnlySpan<char> text) => PostgresNumericTextDecimal.Parse(text);

    internal static decimal Create(UInt128 coefficient, long exponent, bool negative,
        int declaredScale = PostgresNumericProtocol.MaximumDecimalScale)
    {
        while (exponent < PostgresNumericProtocol.ZeroExponent && coefficient % PostgresNumericProtocol.DecimalRadix == PostgresNumericProtocol.ZeroCoefficient)
        {
            coefficient /= PostgresNumericProtocol.DecimalRadix;
            exponent++;
        }
        if (exponent < -Math.Min(declaredScale, PostgresNumericProtocol.MaximumDecimalScale)
            || exponent > PostgresNumericProtocol.MaximumDecimalScale)
        {
            throw Unsupported();
        }
        while (exponent > PostgresNumericProtocol.ZeroExponent)
        {
            if (coefficient > PostgresNumericProtocol.MaximumCoefficient / PostgresNumericProtocol.DecimalRadix)
            {
                throw Unsupported();
            }
            coefficient *= PostgresNumericProtocol.DecimalRadix;
            exponent--;
        }
        if (coefficient > PostgresNumericProtocol.MaximumCoefficient)
        {
            throw Unsupported();
        }
        return new decimal(unchecked((int)(uint)coefficient), unchecked((int)(uint)(coefficient >> PostgresNumericProtocol.WordBits)),
            unchecked((int)(uint)(coefficient >> PostgresNumericProtocol.TwoWordBits)), negative, checked((byte)-exponent));
    }

    private static short Digit(ReadOnlySpan<byte> bytes, int index)
        => BinaryPrimitives.ReadInt16BigEndian(bytes[(PostgresNumericProtocol.HeaderBytes + index * sizeof(short))..]);

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
    private static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
}
