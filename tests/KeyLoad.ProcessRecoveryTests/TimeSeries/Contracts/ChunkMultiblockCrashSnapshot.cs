namespace KeyLoad.ProcessRecoveryTests;

internal sealed record ChunkMultiblockCrashSnapshot(SampleChunkWindowResult Window,
    SampleRecord[] RawRows, string RawImage, string[][] EncodedBlocks, string CompleteDigest, long AppliedPosition, Guid StorageIncarnation, DurabilityProfile StorageDurability,
    string OriginalAcknowledgedReceipt, string OriginalAdditionalResult, string? OriginalInflightResult,
    string? HealthyAppendResult, string? HealthyMergeResult)
{
    public required long NativePosition { get; init; }
}
