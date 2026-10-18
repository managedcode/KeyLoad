namespace KeyLoad;

/// <summary>Describes the storage boundary reached by the copies acknowledged in a command receipt.</summary>
public enum DurabilityProfile
{
    /// <summary>The write is buffered and has not reached a durable acknowledgement.</summary>
    Buffered = 0,
    /// <summary>The write survives a process restart on each acknowledging node.</summary>
    ProcessDurable = 1,
    /// <summary>The write has reached the local durable storage boundary on each acknowledging node.</summary>
    LocalDurable = 2
}
