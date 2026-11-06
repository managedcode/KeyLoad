namespace KeyLoad.ProcessRecoveryTests;

internal sealed record SampleChunkCrashSnapshot(SampleChunkWindowResult Window,
    SampleRecord[] RawRows, string RawImage, long AppliedPosition, string OriginalAcknowledgedReceipt,
    string? OriginalInflightResult)
{
    public required long NativePosition { get; init; }
}
