namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRuntimePolicy
{
    internal const int HashBytes = 32;
    internal const int HashWordBytes = sizeof(ulong);
    internal const int HexCharactersPerByte = 2;
    internal const int HexRadix = 16;
    internal const int SqlStateLength = 5;
    internal const int AsciiBits = 8;
    internal const int Published = 2;
    internal const int MiddleDivisor = 2;
    internal const double MedianPercentile = 0.5;
    internal const double P95Percentile = 0.95;
    internal const double P99Percentile = 0.99;
    internal const double MillisecondsPerSecond = 1000;
    internal const char DigitStart = '0';
    internal const char DigitEnd = '9';
    internal const char UpperStart = 'A';
    internal const char UpperEnd = 'Z';
    internal const char HexUpperEnd = 'F';
    internal const char LowerStart = 'a';
    internal const char HexLowerEnd = 'f';
    internal const int HexLetterOffset = 10;
    internal const int TotalAttempts = TimeSeriesIntensiveProfile.RepetitionCount * TimeSeriesIntensiveProfile.OperationCount;
}
