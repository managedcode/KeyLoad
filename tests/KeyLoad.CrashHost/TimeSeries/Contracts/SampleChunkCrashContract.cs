namespace KeyLoad.CrashHost;

internal static class SampleChunkCrashContract
{
    internal const string PrepareSeal = "sample-chunk-prepare-seal";
    internal const string FaultSeal = "sample-chunk-fault-seal";
    internal const string RecoverSeal = "sample-chunk-recover-seal";
    internal const string PrepareMerge = "sample-chunk-prepare-merge";
    internal const string FaultMerge = "sample-chunk-fault-merge";
    internal const string RecoverMerge = "sample-chunk-recover-merge";
    internal const string Verify = "sample-chunk-verify";
    internal const string StateFile = "sample-chunk-state.json";
    internal const string OriginalOperationFile = "sample-chunk-operation.bin";
    internal const string OriginalReceiptFile = "sample-chunk-receipt.bin";
    internal const string PreparedFile = "sample-chunk-prepared.json";
    internal const string RecoveredFile = "sample-chunk-recovered.json";
    internal const string HealthyFile = "sample-chunk-healthy.json";
    internal const string FinalFile = "sample-chunk-final.json";
    internal const string Invalid = "The native chunk recovered state or original receipt is inconsistent.";
    internal const string Set = "chunk-process-metrics";
    internal const string Series = "chunk-process-series";
    internal const string Tags = "{\"unit\":\"literal\"}";
    private const int CorpusYear = 2030;
    private const int CorpusMonth = 1;
    private const int CorpusDay = 1;
    private const int CorpusHour = 0;
    private const int WindowHours = 1;
    private const int OffsetHours = -5;
    private const double FirstValue = 2;
    private const double SecondValue = 4;
    private const double NegativeZero = -0d;
    private const string WindowIdText = "4a65317b-4d36-4410-b28b-207800000001";
    private const string AcknowledgedIdText = "4a65317b-4d36-4410-b28b-207800000002";
    private const string InflightIdText = "4a65317b-4d36-4410-b28b-207800000003";
    private const string FirstId = "chunk-a";
    private const string EqualId = "chunk-offset";
    private const string LateId = "chunk-late";
    internal const int MaximumRecords = 4096;
    internal const int SampleLimit = 32;
    internal const int MutationIndex = 0;
    internal const int PositionStep = 1;
    internal const long AppendedRevision = 3;
    internal const long SealedRevision = 4;
    internal const long CorrectedRevision = 5;
    internal const long MergedRevision = 6;
    internal const long SealedGeneration = 1;
    internal const long MergedGeneration = 2;
    internal static readonly Guid WindowId = Guid.Parse(WindowIdText);
    internal static readonly Guid AcknowledgedId = Guid.Parse(AcknowledgedIdText);
    internal static readonly Guid InflightId = Guid.Parse(InflightIdText);
    internal static readonly DateTimeOffset From = new(CorpusYear, CorpusMonth, CorpusDay, CorpusHour, CorpusHour, CorpusHour, TimeSpan.Zero);
    internal static readonly DateTimeOffset Until = From.AddHours(WindowHours);
    [KeyLoad.ImmutableTemporalData]
    private static readonly TimeSpan CorpusOffset = TimeSpan.FromHours(OffsetHours);
    internal static PartitionRef Partition => SampleRetentionCrashScenario.Partition;
    internal static SampleData First => new(FirstId, From, FirstValue);
    internal static SampleData Equal => new(EqualId, From.ToOffset(CorpusOffset), SecondValue);
    internal static SampleData Late => new(LateId, From.AddTicks(PositionStep), NegativeZero);
    internal static bool IsMode(string mode) => mode is PrepareSeal or FaultSeal or RecoverSeal
        or PrepareMerge or FaultMerge or RecoverMerge or Verify;
    internal static string[] Modes(bool merge) =>
        [merge ? PrepareMerge : PrepareSeal, merge ? FaultMerge : FaultSeal,
            merge ? RecoverMerge : RecoverSeal, Verify];
}
