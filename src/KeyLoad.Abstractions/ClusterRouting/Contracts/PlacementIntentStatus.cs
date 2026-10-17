namespace KeyLoad;

/// <summary>Defines the bounded PlacementIntentStatus contract.</summary>
public enum PlacementIntentStatus
{
    /// <summary>Identifies the absence of an admitted intent phase.</summary>
    None = 0,
    /// <summary>Identifies the Pending durable phase.</summary>
    Pending = 1,
    /// <summary>Identifies the Applying durable phase.</summary>
    Applying = 2,
    /// <summary>Identifies the Paused durable phase.</summary>
    Paused = 3,
    /// <summary>Identifies the Completed durable phase.</summary>
    Completed = 4
}
