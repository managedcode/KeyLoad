namespace KeyLoad;

/// <summary>Identifies the effect of parking a strict ordering head.</summary>
public enum QueueParkedHeadPolicy
{
    /// <summary>Releases ordering while preserving protected pending or dead-letter data.</summary>
    Continue,
    /// <summary>Retains the parked head until an explicit authorized transition.</summary>
    Block
}
