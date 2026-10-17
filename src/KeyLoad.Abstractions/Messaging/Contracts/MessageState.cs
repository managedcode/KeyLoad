namespace KeyLoad;

/// <summary>Identifies the persisted lifecycle state of a queued message.</summary>
public enum MessageState
{
    /// <summary>The message is waiting for its not-before time.</summary>
    Scheduled,
    /// <summary>The message is available for delivery.</summary>
    Ready,
    /// <summary>The message is currently leased to a consumer.</summary>
    Leased,
    /// <summary>The message was acknowledged.</summary>
    Acked,
    /// <summary>The message exceeded its retry policy.</summary>
    DeadLettered,
    /// <summary>The message was cancelled.</summary>
    Cancelled,
    /// <summary>The message expired before completion.</summary>
    Expired,
    /// <summary>The exhausted message retains its body while dead-letter admission is full.</summary>
    PendingDeadLetter
}
