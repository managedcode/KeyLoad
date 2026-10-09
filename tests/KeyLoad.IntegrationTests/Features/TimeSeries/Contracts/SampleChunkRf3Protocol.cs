namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRf3Protocol
{
    internal const string FixtureKey = "sample-chunk-native-cold";
    internal const string ColdScenario = "sample-chunk-generation-cold";
    internal const int WindowHours = 1;
    internal const int AlternateOffsetHours = -5;
    internal const int PlacementVersion = 1;
    internal const long FirstSequence = 1;
    internal const double FirstValue = 2;
    internal const double SecondValue = 4;
    internal const double NegativeZero = -0d;
    internal const long OpenRevision = 1;
    internal const long AppendedRevision = 3;
    internal const long SealedRevision = 4;
    internal const long CorrectedRevision = 5;
    internal const long MergedRevision = 6;
    internal const long SealedGeneration = 1;
    internal const long MergedGeneration = 2;
    internal const long InitialSequence = 2;
    internal const long CorrectedSequence = 3;
    internal const int OutputLimit = 32;
    internal const int PollMilliseconds = 100;
    internal const string FirstId = "chunk-first";
    internal const string EqualId = "chunk-offset";
    internal const string LateId = "chunk-late";
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(PollMilliseconds);
}
