namespace KeyLoad;

/// <summary>Describes the durability guarantee recorded for a committed command.</summary>
public enum DurabilityProfile
{
    /// <summary>The write is buffered and has not reached a durable acknowledgement.</summary>
    Buffered,
    /// <summary>The write survives a process restart on the acknowledging node.</summary>
    ProcessDurable,
    /// <summary>The write has reached the local durable storage boundary.</summary>
    LocalDurable,
    /// <summary>A quorum acknowledged process-durable state.</summary>
    QuorumProcessDurable,
    /// <summary>A quorum acknowledged durable state.</summary>
    QuorumDurable
}
