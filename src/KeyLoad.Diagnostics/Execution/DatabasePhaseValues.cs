namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Names immutable schema and arithmetic values used by the phase bank.</summary>
internal static class DatabasePhaseValues
{
    internal const int FirstBoundaryIndex = 0;
    internal const int FirstCasAttempt = 0;
    internal const int FirstStripeIndex = 0;
    internal const int FirstHistogramLane = 0;
    internal const int FirstPhaseIndex = 0;
    internal const long MinimumElapsedTicks = 0;
    internal const long MinimumTimestampFrequency = 0;
    internal const long MinimumStartTimestamp = 0;
    internal const long MinimumCounterValue = 0;
    internal const long DisabledSnapshotFrequency = 0;
    internal const long DisabledSnapshotMonotonicTimestamp = 0;
    internal const long EmptyCounterTotal = 0;
    internal const int CounterIncrement = 1;

    internal const int FiftyMicroseconds = 50;
    internal const int OneHundredMicroseconds = 100;
    internal const int TwoHundredFiftyMicroseconds = 250;
    internal const int FiveHundredMicroseconds = 500;
    internal const int OneThousandMicroseconds = 1_000;
    internal const int TwoThousandMicroseconds = 2_000;
    internal const int FiveThousandMicroseconds = 5_000;
    internal const int TenThousandMicroseconds = 10_000;
    internal const int TwentyThousandMicroseconds = 20_000;
    internal const int FiftyThousandMicroseconds = 50_000;
    internal const int OneHundredThousandMicroseconds = 100_000;
    internal const int TwoHundredFiftyThousandMicroseconds = 250_000;
    internal const int FiveHundredThousandMicroseconds = 500_000;
    internal const int OneMillionMicroseconds = 1_000_000;
    internal const int FiveMillionMicroseconds = 5_000_000;
}
