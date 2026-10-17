namespace KeyLoad;

/// <summary>Defines the starting point used when configuring or seeking a subscription.</summary>
public enum SubscriptionStart
{
    /// <summary>Start from the earliest retained source position.</summary>
    FromBeginning,
    /// <summary>Start from the source tail at the time the operation commits.</summary>
    FromNow,
    /// <summary>Start from the position encoded by a supplied cursor.</summary>
    FromCursor
}
