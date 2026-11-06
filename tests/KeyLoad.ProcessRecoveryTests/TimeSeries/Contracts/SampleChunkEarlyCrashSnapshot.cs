namespace KeyLoad.ProcessRecoveryTests;

internal sealed record SampleChunkEarlyCrashSnapshot(SampleChunkWindowResult? Window,
    SampleRecord[] RawRows, string RawImage, long AppliedPosition, string OriginalAcknowledgedResult,
    string? OriginalInflightResult)
{
    public required long NativePosition { get; init; }
}
