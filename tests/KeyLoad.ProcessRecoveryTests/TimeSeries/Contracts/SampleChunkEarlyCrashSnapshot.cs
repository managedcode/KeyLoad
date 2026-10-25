namespace KeyLoad.ProcessRecoveryTests;

internal sealed record SampleChunkEarlyCrashSnapshot(SampleChunkWindowResult? Window,
    SampleRecord[] RawRows, string RawImage, long Position, string OriginalAcknowledgedResult,
    string? OriginalInflightResult);
