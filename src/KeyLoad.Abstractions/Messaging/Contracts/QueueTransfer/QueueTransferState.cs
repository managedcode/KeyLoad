namespace KeyLoad;

/// <summary>Identifies a durable cross-partition queue transfer lifecycle.</summary>
public enum QueueTransferState
{
    /// <summary>The source intent is committed and awaits a destination receipt.</summary>
    OutputPending,
    /// <summary>The source committed a valid receipt from the destination partition.</summary>
    Delivered
}
