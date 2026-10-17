namespace KeyLoad;

/// <summary>Identifies the logged bounded retry timing profile.</summary>
public enum QueueRetryJitter
{
    /// <summary>Retains fixed exponential NACK timing and original lease-reclaim behavior.</summary>
    None,
    /// <summary>Records a deterministic server-keyed choice across the full positive cap.</summary>
    Full
}
