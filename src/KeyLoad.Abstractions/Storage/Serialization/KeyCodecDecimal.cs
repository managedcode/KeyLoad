using System.Globalization;
using System.Numerics;
using System.Text;
using static KeyLoad.Storage.KeyCodecDecimalTokens;
using static KeyLoad.Storage.KeyCodecReadPrimitives;

namespace KeyLoad.Storage;

internal static class KeyCodecDecimal
{
    private const int MinimumExponent = -27;
    private const int MaximumExponent = 29;
    private const int MaximumScale = 28;
    private const string MaximumCoefficient = "79228162514264337593543950335";

    internal static void Write(Stream stream, decimal value)
    {
        stream.WriteByte(KeyCodecTokens.DecimalTag);
        if (value == ZeroNumber)
        {
            stream.WriteByte(ZeroSign);
            return;
        }

        var negative = value < ZeroNumber;
        stream.WriteByte(negative ? (byte)NegativeSign : (byte)PositiveSign);
        var parts = decimal.GetBits(decimal.Abs(value));
        var magnitude = (BigInteger)(uint)parts[LowWordIndex] | (BigInteger)(uint)parts[MiddleWordIndex] << MiddleWordShift | (BigInteger)(uint)parts[HighWordIndex] << HighWordShift;
        var scale = (parts[FlagsWordIndex] >> ScaleShift) & ScaleMask;
        var digits = magnitude.ToString(CultureInfo.InvariantCulture);
        var exponent = digits.Length - scale;
        digits = digits.TrimEnd(ZeroDigit);
        WriteOrderedDigit(stream, negative, (byte)(exponent + ExponentBias));
        foreach (var digit in digits)
        {
            WriteOrderedDigit(stream, negative, (byte)(digit - ZeroDigit + DigitOffset));
        }

        WriteOrderedDigit(stream, negative, DigitTerminator);
    }

    internal static decimal Read(ReadOnlySpan<byte> key, ref int offset)
    {
        var sign = ReadByte(key, ref offset);
        if (sign == ZeroSign)
        {
            return ZeroNumber;
        }

        if (sign is not (NegativeSign or PositiveSign))
        {
            throw BadKey();
        }

        var exponent = ReadExponent(key, ref offset, sign) - ExponentBias;
        var digits = ReadDigits(key, ref offset, sign);
        if (digits[FirstDigitIndex] == ZeroDigit || digits[^LastDigitFromEnd] == ZeroDigit)
        {
            throw BadKey();
        }

        var scale = digits.Length - exponent;
        if (!IsRepresentableMagnitude(digits, exponent, scale))
        {
            throw BadKey();
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"{(sign == NegativeSign ? NegativeSignText : string.Empty)}{digits}{ExponentSeparator}{exponent - digits.Length}");
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value == ZeroNumber || !HasExpectedScaleAndSign(value, sign, Math.Max(MinimumScale, scale)))
        {
            throw BadKey();
        }

        return value;
    }

    private static bool IsRepresentableMagnitude(string digits, int exponent, int scale)
    {
        if (exponent is < MinimumExponent or > MaximumExponent || scale > MaximumScale)
        {
            return false;
        }

        var integerDigits = Math.Max(digits.Length, exponent);
        if (integerDigits < MaximumCoefficient.Length)
        {
            return true;
        }

        for (var index = FirstDigitIndex; index < MaximumCoefficient.Length; index++)
        {
            var digit = index < digits.Length ? digits[index] : ZeroDigit;
            if (digit != MaximumCoefficient[index])
            {
                return digit < MaximumCoefficient[index];
            }
        }

        return true;
    }

    private static bool HasExpectedScaleAndSign(decimal value, byte sign, int expectedScale)
    {
        Span<int> parts = stackalloc int[DecimalWordCount];
        _ = decimal.GetBits(value, parts);
        var actualScale = (parts[FlagsWordIndex] >> ScaleShift) & ScaleMask;
        var isNegative = (parts[FlagsWordIndex] & int.MinValue) != NoSignBits;
        return actualScale == expectedScale && isNegative == (sign == NegativeSign);
    }

    private static byte ReadExponent(ReadOnlySpan<byte> key, ref int offset, byte sign)
    {
        var exponent = ReadByte(key, ref offset);
        return sign == NegativeSign ? (byte)~exponent : exponent;
    }

    private static string ReadDigits(ReadOnlySpan<byte> key, ref int offset, byte sign)
    {
        var digits = new StringBuilder(MaximumEncodedDigits);
        while (true)
        {
            var digit = ReadByte(key, ref offset);
            if (sign == NegativeSign)
            {
                digit = (byte)~digit;
            }

            if (digit == DigitTerminator)
            {
                break;
            }

            if (digit > MaximumEncodedDigit || digits.Length == MaximumEncodedDigits)
            {
                throw BadKey();
            }

            digits.Append((char)(ZeroDigit + digit - DigitOffset));
        }

        if (digits.Length == EmptyDigitCount)
        {
            throw BadKey();
        }

        return digits.ToString();
    }

    private static void WriteOrderedDigit(Stream stream, bool negative, byte value)
        => stream.WriteByte(negative ? (byte)~value : value);
}
