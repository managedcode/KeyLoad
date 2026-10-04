using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Core.Features.RelationalStorage;

internal static class RelationalDecimalValidation
{
    private const char Minus = '-';
    private const char DecimalPoint = '.';
    private const char LowerExponent = 'e';
    private const char UpperExponent = 'E';
    private const char Zero = '0';
    private const int MaximumSignificantDigits = 29;
    private const int MaximumScale = 28;
    private const uint DecimalRadix = 10;
    private const int MiddleBits = 32;
    private const int UpperBits = 64;
    private static readonly UInt128 MaximumCoefficient = ((UInt128)uint.MaxValue << UpperBits)
        | ((UInt128)uint.MaxValue << MiddleBits) | uint.MaxValue;

    internal static bool IsExact(JsonElement value)
    {
        if (!value.TryGetDecimal(out _))
        {
            return false;
        }
        var raw = value.GetRawText().AsSpan();
        if (raw[0] == Minus)
        {
            raw = raw[1..];
        }
        var exponentPosition = raw.IndexOfAny(LowerExponent, UpperExponent);
        var exponent = 0;
        if (exponentPosition >= 0 && !int.TryParse(raw[(exponentPosition + 1)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
        {
            return false;
        }
        var mantissa = exponentPosition < 0 ? raw : raw[..exponentPosition];
        var decimalPoint = mantissa.IndexOf(DecimalPoint);
        var first = FirstNonzero(mantissa);
        if (first < 0)
        {
            return true;
        }
        var last = LastNonzero(mantissa);
        var significantDigits = last - first + 1 - (decimalPoint >= first && decimalPoint <= last ? 1 : 0);
        var trailingZeros = mantissa.Length - last - 1 - (decimalPoint > last ? 1 : 0);
        var fractionalDigits = decimalPoint < 0 ? 0 : mantissa.Length - decimalPoint - 1;
        var scale = (long)fractionalDigits - trailingZeros - exponent;
        if (significantDigits > MaximumSignificantDigits || scale > MaximumScale || significantDigits - Math.Min(scale, 0) > MaximumSignificantDigits)
        {
            return false;
        }
        return CoefficientFits(mantissa[first..(last + 1)], scale);
    }

    private static int FirstNonzero(ReadOnlySpan<char> mantissa)
    {
        for (var index = 0; index < mantissa.Length; index++)
        {
            if (mantissa[index] is not (Zero or DecimalPoint))
            {
                return index;
            }
        }
        return -1;
    }

    private static int LastNonzero(ReadOnlySpan<char> mantissa)
    {
        for (var index = mantissa.Length - 1; index >= 0; index--)
        {
            if (mantissa[index] is not (Zero or DecimalPoint))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool CoefficientFits(ReadOnlySpan<char> digits, long scale)
    {
        UInt128 coefficient = 0;
        foreach (var digit in digits)
        {
            if (digit != DecimalPoint)
            {
                coefficient = (coefficient * DecimalRadix) + (uint)(digit - Zero);
            }
        }
        for (; scale < 0; scale++)
        {
            coefficient *= DecimalRadix;
        }
        return coefficient <= MaximumCoefficient;
    }
}
