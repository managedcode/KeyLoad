namespace KeyLoad.Server.Features.Search;

/// <summary>Actual disposable execution-generation boundaries for owned process recovery trials.</summary>
internal enum TextFaultStage
{
    /// <summary>The physical owner receipt is durably written before execution creation.</summary>
    OwnerFlushed,
    /// <summary>The first actual posting in this generation has been written.</summary>
    PostingWritten,
    /// <summary>Execution handles settled and their complete owned inventory is durable.</summary>
    InventoryFlushed,
    /// <summary>The complete source-bound manifest has been atomically published.</summary>
    ManifestPublished,
    /// <summary>The verified generation became current before obsolete generation cleanup.</summary>
    GenerationActivated,
    /// <summary>The complete original incremental intent has been durably persisted.</summary>
    IncrementalIntentFlushed,
    /// <summary>The first actual incremental execution deletion has completed.</summary>
    DeletionWritten,
    /// <summary>The first actual incremental execution addition has completed.</summary>
    AdditionWritten,
    /// <summary>The original canonical journal ACK passed complete current checkpoint validation.</summary>
    CanonicalCheckpointAcknowledged,
    /// <summary>The actual original pending intent file has been removed after its validated ACK.</summary>
    PendingIntentRetired,
    /// <summary>The actual retained original canonical35 receipt has joined.</summary>
    CanonicalOnlinePublicationAcknowledged = 10,
    /// <summary>The exact online catalog pending envelope has flushed and passed readback.</summary>
    OnlineCatalogPendingFlushed = 11
}
