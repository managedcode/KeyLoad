namespace KeyLoad;

/// <summary>Identifies the canonical dispatch ordering contract of a queue.</summary>
public enum QueueOrderingProfile
{
    /// <summary>Dispatches independent messages in current ready order.</summary>
    CompetingConsumers,
    /// <summary>Retains a single execution eligible head per explicit ordering key.</summary>
    StrictPerKey
}
