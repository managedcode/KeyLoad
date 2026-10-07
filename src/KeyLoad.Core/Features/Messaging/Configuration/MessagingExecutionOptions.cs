namespace KeyLoad.Core;

/// <summary>Node-owned topic publication, recurring work and retry computation admission.</summary>
[ConfigurationOptions]
public sealed record MessagingExecutionOptions
{
    /// <summary>The centrally bound messaging execution section.</summary>
    public const string SectionName = "KeyLoad:MessagingExecution";
    /// <summary>Safe rejection raised before messaging execution is admitted.</summary>
    public const string ValidationMessage = "Messaging work limits must be positive and the retry exponent must remain within its arithmetic bound.";

    private const int DefaultMaximumTopicEvents = 256;
    private const int DefaultMaximumOccurrenceCatchUp = 32;
    private const int DefaultMaximumRetryExponent = 20;
    private const int DefaultQueueScanPageSize = 256;
    private const int MaximumQueueScanPageSize = 100_000;
    private const int DefaultMaximumDeliveryItems = 100;
    private const int MaximumReceiveLanesCeiling = 8;
    private const int MinimumWorkCount = 1;
    private const int MinimumRetryExponent = 0;
    private const int MaximumSafeRetryExponent = 20;

    /// <summary>Gets the maximum events in one authorized topic publication.</summary>
    public int MaximumTopicEvents { get; init; } = DefaultMaximumTopicEvents;
    /// <summary>Gets the maximum recurring occurrences emitted in one atomic catch-up operation.</summary>
    public int MaximumOccurrenceCatchUp { get; init; } = DefaultMaximumOccurrenceCatchUp;
    /// <summary>Gets the capped retry exponent before applying the persisted queue's maximum delay.</summary>
    public int MaximumRetryExponent { get; init; } = DefaultMaximumRetryExponent;

    /// <summary>Gets the maximum scheduled or leased entries swept by one atomic queue operation.</summary>
    public int QueueScanPageSize { get; init; } = DefaultQueueScanPageSize;

    /// <summary>Maximum messages admitted to one queue receive request.</summary>
    public int MaximumReceiveMessages { get; init; } = DefaultMaximumDeliveryItems;
    /// <summary>Maximum independent lanes in one sequential receive composition.</summary>
    public int MaximumReceiveLanes { get; init; } = MaximumReceiveLanesCeiling;
    /// <summary>Maximum events admitted to one subscription receive request.</summary>
    public int MaximumReceiveEvents { get; init; } = DefaultMaximumDeliveryItems;

    /// <summary>Checks independent work budgets and the existing retry arithmetic safety ceiling.</summary>
    public bool IsValid() => MaximumTopicEvents >= MinimumWorkCount
        && MaximumOccurrenceCatchUp >= MinimumWorkCount
        && MaximumRetryExponent >= MinimumRetryExponent && MaximumRetryExponent <= MaximumSafeRetryExponent
        && QueueScanPageSize >= MinimumWorkCount && QueueScanPageSize <= MaximumQueueScanPageSize
        && MaximumReceiveMessages is >= MinimumWorkCount and <= DefaultMaximumDeliveryItems
        && MaximumReceiveEvents is >= MinimumWorkCount and <= DefaultMaximumDeliveryItems
        && MaximumReceiveLanes is >= MinimumWorkCount and <= MaximumReceiveLanesCeiling;

    /// <summary>Rejects invalid settings before database recovery or command admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new InvalidOperationException(ValidationMessage); }
    }
}
