using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Core.Features.RelationalStorage;

internal static class RelationalDecimalValidation
{
    private const int FirstCharacterIndex = 0;
    private const int SignCharacters = 1;
    private const int ExponentSeparatorCharacters = 1;
    private const int NoExponent = 0;
    private const int CharacterRangeAdjustment = 1;
    private const int DecimalPointCharacters = 1;
    private const int NoDecimalPointCharacters = 0;
    private const int NoFractionalDigits = 0;
    private const int MinimumDecimalScale = 0;
    private const int MissingCharacterIndex = -1;
    private const int NoCoefficient = 0;
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
        if (raw[FirstCharacterIndex] == Minus)
        {
            raw = raw[SignCharacters..];
        }
        var exponentPosition = raw.IndexOfAny(LowerExponent, UpperExponent);
        var exponent = NoExponent;
        if (exponentPosition >= FirstCharacterIndex && !int.TryParse(raw[(exponentPosition + ExponentSeparatorCharacters)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
        {
            return false;
        }
        var mantissa = exponentPosition < FirstCharacterIndex ? raw : raw[..exponentPosition];
        var decimalPoint = mantissa.IndexOf(DecimalPoint);
        var first = FirstNonzero(mantissa);
        if (first < FirstCharacterIndex)
        {
            return true;
        }
        var last = LastNonzero(mantissa);
        var significantDigits = last - first + CharacterRangeAdjustment - (decimalPoint >= first && decimalPoint <= last ? DecimalPointCharacters : NoDecimalPointCharacters);
        var trailingZeros = mantissa.Length - last - CharacterRangeAdjustment - (decimalPoint > last ? DecimalPointCharacters : NoDecimalPointCharacters);
        var fractionalDigits = decimalPoint < FirstCharacterIndex ? NoFractionalDigits : mantissa.Length - decimalPoint - CharacterRangeAdjustment;
        var scale = (long)fractionalDigits - trailingZeros - exponent;
        if (significantDigits > MaximumSignificantDigits || scale > MaximumScale || significantDigits - Math.Min(scale, MinimumDecimalScale) > MaximumSignificantDigits)
        {
            return false;
        }
        return CoefficientFits(mantissa[first..(last + CharacterRangeAdjustment)], scale);
    }

    private static int FirstNonzero(ReadOnlySpan<char> mantissa)
    {
        for (var index = FirstCharacterIndex; index < mantissa.Length; index++)
        {
            if (mantissa[index] is not (Zero or DecimalPoint))
            {
                return index;
            }
        }
        return MissingCharacterIndex;
    }

    private static int LastNonzero(ReadOnlySpan<char> mantissa)
    {
        for (var index = mantissa.Length - CharacterRangeAdjustment; index >= FirstCharacterIndex; index--)
        {
            if (mantissa[index] is not (Zero or DecimalPoint))
            {
                return index;
            }
        }
        return MissingCharacterIndex;
    }

    private static bool CoefficientFits(ReadOnlySpan<char> digits, long scale)
    {
        UInt128 coefficient = NoCoefficient;
        foreach (var digit in digits)
        {
            if (digit != DecimalPoint)
            {
                coefficient = (coefficient * DecimalRadix) + (uint)(digit - Zero);
            }
        }
        for (; scale < MinimumDecimalScale; scale++)
        {
            coefficient *= DecimalRadix;
        }
        return coefficient <= MaximumCoefficient;
    }
}
