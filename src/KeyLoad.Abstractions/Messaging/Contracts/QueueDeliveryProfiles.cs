namespace KeyLoad;

/// <summary>Identifies the canonical dispatch ordering contract of a queue.</summary>
public enum QueueOrderingProfile
{
    /// <summary>Dispatches independent messages in current ready order.</summary>
    CompetingConsumers,
    /// <summary>Retains a single native eligible head per explicit ordering key.</summary>
    StrictPerKey
}

/// <summary>Identifies the effect of parking a strict ordering head.</summary>
public enum QueueParkedHeadPolicy
{
    /// <summary>Releases ordering while preserving protected pending or dead-letter data.</summary>
    Continue,
    /// <summary>Retains the parked head until an explicit authorized transition.</summary>
    Block
}

/// <summary>Identifies the logged bounded retry timing profile.</summary>
public enum QueueRetryJitter
{
    /// <summary>Retains fixed exponential NACK timing and original lease-reclaim behavior.</summary>
    None,
    /// <summary>Records a deterministic server-keyed choice across the full positive cap.</summary>
    Full
}
