using System.Globalization;
using System.Numerics;
using System.Text;
using static KeyLoad.Storage.KeyCodecReadPrimitives;

namespace KeyLoad.Storage;

internal static class KeyCodecDecimal
{
    internal static void Write(Stream stream, decimal value)
    {
        stream.WriteByte(0x31);
        if (value == 0)
        {
            stream.WriteByte(1);
            return;
        }

        var negative = value < 0;
        stream.WriteByte(negative ? (byte)0 : (byte)2);
        var parts = decimal.GetBits(decimal.Abs(value));
        var magnitude = (BigInteger)(uint)parts[0] | (BigInteger)(uint)parts[1] << 32 | (BigInteger)(uint)parts[2] << 64;
        var scale = (parts[3] >> 16) & 0xFF;
        var digits = magnitude.ToString(CultureInfo.InvariantCulture);
        var exponent = digits.Length - scale;
        digits = digits.TrimEnd('0');
        WriteOrderedDigit(stream, negative, (byte)(exponent + 64));
        foreach (var digit in digits)
        {
            WriteOrderedDigit(stream, negative, (byte)(digit - '0' + 1));
        }

        WriteOrderedDigit(stream, negative, 0);
    }

    internal static decimal Read(ReadOnlySpan<byte> key, ref int offset)
    {
        var sign = ReadByte(key, ref offset);
        if (sign == 1)
        {
            return 0;
        }

        if (sign is not (0 or 2))
        {
            throw BadKey();
        }

        var exponent = ReadExponent(key, ref offset, sign);
        var digits = ReadDigits(key, ref offset, sign);
        return decimal.Parse($"{(sign == 0 ? "-" : "")}{digits}e{exponent - 64 - digits.Length}",
            NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static byte ReadExponent(ReadOnlySpan<byte> key, ref int offset, byte sign)
    {
        var exponent = ReadByte(key, ref offset);
        return sign == 0 ? (byte)~exponent : exponent;
    }

    private static string ReadDigits(ReadOnlySpan<byte> key, ref int offset, byte sign)
    {
        var digits = new StringBuilder(29);
        while (true)
        {
            var digit = ReadByte(key, ref offset);
            if (sign == 0)
            {
                digit = (byte)~digit;
            }

            if (digit == 0)
            {
                break;
            }

            if (digit > 10 || digits.Length == 29)
            {
                throw BadKey();
            }

            digits.Append((char)('0' + digit - 1));
        }

        if (digits.Length == 0)
        {
            throw BadKey();
        }

        return digits.ToString();
    }

    private static void WriteOrderedDigit(Stream stream, bool negative, byte value)
        => stream.WriteByte(negative ? (byte)~value : value);
}
