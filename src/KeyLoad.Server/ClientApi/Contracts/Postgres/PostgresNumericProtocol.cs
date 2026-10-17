namespace KeyLoad.Server;

internal static class PostgresNumericProtocol
{
    internal const int Oid = 1700;
    internal const int FirstIndex = 0;
    internal const int AbsentDigit = -1;
    internal const int ZeroCount = 0;
    internal const int ZeroScale = 0;
    internal const int ZeroDigit = 0;
    internal const long ZeroExponent = 0;
    internal static readonly UInt128 ZeroCoefficient = UInt128.Zero;
    internal const int HeaderBytes = sizeof(short) * 4;
    internal const int CountOffset = 0;
    internal const int WeightOffset = sizeof(short);
    internal const int SignOffset = sizeof(short) * 2;
    internal const int ScaleOffset = sizeof(short) * 3;
    internal const int Radix = 10000;
    internal const int RadixDigits = 4;
    internal const int DecimalRadix = 10;
    internal const int MaximumCoefficientDigits = 32;
    internal const int MaximumCoefficientGroups = 8;
    internal const int MaximumDecimalScale = 28;
    internal const ushort Positive = 0x0000;
    internal const ushort Negative = 0x4000;
    internal const ushort NaN = 0xC000;
    internal const ushort PositiveInfinity = 0xD000;
    internal const ushort NegativeInfinity = 0xF000;
    internal const int MaximumDisplayScale = 0x3FFF;
    internal const int WordBits = sizeof(uint) * 8;
    internal const int TwoWordBits = WordBits * 2;
    internal const string NaNText = "NaN";
    internal const string InfinityText = "Infinity";
    internal const string ShortInfinityText = "inf";
    internal const char Plus = '+';
    internal const char Minus = '-';
    internal const char Point = '.';
    internal const char Exponent = 'e';
    internal const char ExponentUpper = 'E';
    internal const char Zero = '0';
    internal const char Nine = '9';
    internal static readonly UInt128 MaximumCoefficient = ((UInt128)uint.MaxValue << TwoWordBits)
        | ((UInt128)uint.MaxValue << WordBits) | uint.MaxValue;
}
