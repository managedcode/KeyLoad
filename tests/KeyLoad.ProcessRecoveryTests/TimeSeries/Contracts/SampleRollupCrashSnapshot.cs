namespace KeyLoad.ProcessRecoveryTests;

internal sealed record SampleRollupCrashSnapshot(SampleRecord[] Samples, SampleRollupResult Rollup,
    string RawImage, long AppliedPosition, string AcknowledgedReceipt)
{
    public required long NativePosition { get; init; }
}
