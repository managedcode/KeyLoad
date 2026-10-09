namespace KeyLoad.Server.Features.Search;

/// <summary>Actual disposable native-generation boundaries for owned process recovery trials.</summary>
internal enum NativeTextFaultStage
{
    /// <summary>The physical owner receipt is durably written before native creation.</summary>
    OwnerFlushed,
    /// <summary>The first actual posting in this generation has been written.</summary>
    NativePostingWritten,
    /// <summary>Native handles settled and their complete owned inventory is durable.</summary>
    NativeInventoryFlushed,
    /// <summary>The complete source-bound manifest has been atomically published.</summary>
    ManifestPublished,
    /// <summary>The verified generation became current before obsolete generation cleanup.</summary>
    GenerationActivated,
    /// <summary>The complete original incremental intent has been durably persisted.</summary>
    IncrementalIntentFlushed,
    /// <summary>The first actual incremental native deletion has completed.</summary>
    NativeDeletionWritten,
    /// <summary>The first actual incremental native addition has completed.</summary>
    NativeAdditionWritten,
    /// <summary>The original canonical journal ACK passed complete current checkpoint validation.</summary>
    CanonicalCheckpointAcknowledged,
    /// <summary>The actual original pending intent file has been removed after its validated ACK.</summary>
    PendingIntentRetired
}
