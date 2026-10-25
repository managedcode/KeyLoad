namespace KeyLoad.ProcessRecoveryTests;

internal sealed record SampleChunkCrashSnapshot(SampleChunkWindowResult Window,
    SampleRecord[] RawRows, string RawImage, long Position, string OriginalAcknowledgedReceipt,
    string? OriginalInflightResult);
