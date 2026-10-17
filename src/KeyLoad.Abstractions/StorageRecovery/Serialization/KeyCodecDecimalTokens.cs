namespace KeyLoad.Storage;

// Decimal word layout and sortable v1 sign/digit identities are immutable wire structure.
internal static class KeyCodecDecimalTokens
{
    internal const int NegativeSign = 0;
    internal const int ZeroSign = 1;
    internal const int PositiveSign = 2;
    internal const int ZeroNumber = 0;
    internal const int LowWordIndex = 0;
    internal const int MiddleWordIndex = 1;
    internal const int HighWordIndex = 2;
    internal const int FlagsWordIndex = 3;
    internal const int DecimalWordCount = 4;
    internal const int MiddleWordShift = 32;
    internal const int HighWordShift = 64;
    internal const int ScaleShift = 16;
    internal const int ScaleMask = 0xFF;
    internal const int ExponentBias = 64;
    internal const int DigitOffset = 1;
    internal const int DigitTerminator = 0;
    internal const int MaximumEncodedDigit = 10;
    internal const int MaximumEncodedDigits = 29;
    internal const int FirstDigitIndex = 0;
    internal const int LastDigitFromEnd = 1;
    internal const int MinimumScale = 0;
    internal const int EmptyDigitCount = 0;
    internal const int NoSignBits = 0;
    internal const char ZeroDigit = '0';
    internal const string NegativeSignText = "-";
    internal const string ExponentSeparator = "e";
}
