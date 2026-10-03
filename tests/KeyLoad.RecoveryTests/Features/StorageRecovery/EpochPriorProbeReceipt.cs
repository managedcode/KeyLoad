namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal readonly record struct EpochPriorProbeReceipt(string SourceRevision, int DataEpoch, long Position,
    long AppliedPosition, Guid NodeId, Guid Incarnation, long ReadGeneration, bool DispatchPaused,
    string SigningKeySha256, DurabilityProfile Durability, string? ErrorCode = null);
