using System.Globalization;

namespace KeyLoad.CrashHost;

internal static class SampleRollupCrashContract
{
    internal const string PrepareMode = "sample-rollup-prepare";
    internal const string RefreshFaultMode = "sample-rollup-refresh-fault";
    internal const string DropFaultMode = "sample-rollup-drop-fault";
    internal const string RefreshRecoverMode = "sample-rollup-refresh-recover";
    internal const string DropRecoverMode = "sample-rollup-drop-recover";
    internal const string VerifyMode = "sample-rollup-verify";
    internal const string PreparedFile = "sample-rollup-prepared.json";
    internal const string BeforeFile = "sample-rollup-before.json";
    internal const string RecoveredFile = "sample-rollup-recovered.json";
    internal const string HealthyRefreshFile = "sample-rollup-healthy-refresh.json";
    internal const string HealthyDropFile = "sample-rollup-healthy-drop.json";
    internal const string HealthyFile = "sample-rollup-healthy.json";
    internal const string FinalFile = "sample-rollup-final.json";
    internal const string AcknowledgedReceiptFile = "sample-rollup-acknowledged.bin";
    internal const string OperationFile = "sample-rollup-inflight.bin";
    internal const string RawFile = "sample-rollup-raw.bin";
    internal const string Set = "rollup-process-metrics";
    internal const string Series = "rollup-process-series";
    private const string ThirdTimestampText = "2026-09-30T19:00:30.0000000-05:00";
    internal const string Tags = "{\"unit\":\"literal\"}";
    internal const string Invalid = "The rollup process cut differed from its original state or receipt.";
    internal const string AcknowledgedIdText = "1d9340ec-d5eb-44cb-bcaa-02dd6e62f100";
    internal const string InflightIdText = "1d9340ec-d5eb-44cb-bcaa-02dd6e62f101";
    internal const int MaximumRecords = 4096;
    internal const int SampleLimit = 4;
    internal const int MutationIndex = 0;
    internal const int PositionStep = 1;
    internal static PartitionRef Partition { get; } = SampleRetentionCrashScenario.Partition;
    internal static DateTimeOffset Start { get; } = SampleRetentionCrashScenario.Start;
    internal static DateTimeOffset ThirdTimestamp { get; } = DateTimeOffset.Parse(ThirdTimestampText, CultureInfo.InvariantCulture);
    internal static DateTimeOffset End { get; } = Start.AddMinutes(PositionStep);
    internal static Guid AcknowledgedId { get; } = Guid.Parse(AcknowledgedIdText);
    internal static Guid InflightId { get; } = Guid.Parse(InflightIdText);
    internal static RefreshSampleRollup Refresh(long revision)
        => new(Set, Series, Start, End, revision, SampleLimit);
    internal static DropSampleRollup Drop(long revision) => new(Set, Series, Start, End, revision);
    internal static string[] Modes(bool drop) => [PrepareMode,
        drop ? DropFaultMode : RefreshFaultMode, drop ? DropRecoverMode : RefreshRecoverMode, VerifyMode];
}

internal sealed record SampleRollupCrashSnapshot(SampleRecord[] Samples, SampleRollupResult Rollup,
    string RawImage, long Position, string AcknowledgedReceipt);
