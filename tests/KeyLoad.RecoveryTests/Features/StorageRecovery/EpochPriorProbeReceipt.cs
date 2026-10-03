namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed record EpochPriorProbeReceipt(string SourceRevision, int DataEpoch, long Position,
    long AppliedPosition, Guid NodeId, Guid Incarnation, long ReadGeneration, bool DispatchPaused,
    string SigningKeySha256, DurabilityProfile Durability, string? ErrorCode = null);
