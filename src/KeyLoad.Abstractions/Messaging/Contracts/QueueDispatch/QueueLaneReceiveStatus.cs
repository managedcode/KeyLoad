namespace KeyLoad;

/// <summary>Describes the known result of an independent lane attempt.</summary>
public enum QueueLaneReceiveStatus
{
    /// <summary>The canonical lane claim committed, possibly with no deliveries.</summary>
    Committed,
    /// <summary>The lane returned a definitive safe rejection.</summary>
    Rejected,
    /// <summary>The submitted lane's write outcome is uncertain.</summary>
    Unknown,
    /// <summary>No claim was dispatched after an earlier uncertain attempt.</summary>
    NotAttempted
}
