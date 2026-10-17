namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Protocol
{
    internal const string FixtureKey = "sample-chunk-retention-rollup-cold";
    internal const string ColdScenario = "sample-chunk-retention-rollup-cold";
    internal const long AbsentRevision = 0;
    internal const long FirstRevision = 1;
    internal const long DroppedRevision = 2;
    internal const long FreshWindowRevision = 2;
    internal const long FreshSequence = 4;
    internal const long PartialPurged = 2;
    internal const long FullPurged = 3;
    internal const long OpenGeneration = 0;
    internal const long InitialCount = 3;
    internal const long RemainingCount = 1;
    internal const double InitialSum = 12;
    internal const double InitialAverage = 4;
    internal const double LateValue = 6;
    internal const double Zero = 0;
    internal const double FreshValue = 8;
    internal const string FreshId = "chunk-after-retention";
    internal const string ExpireKind = "expireSamples";
    internal const string AppendKind = "appendSamples";
}
