namespace KeyLoad;

/// <summary>Defines storage, delivery, lease, and retry limits for a work queue.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueuePolicy)]
public sealed record QueuePolicy
{
    private const int DefaultMaxAttempts = 5;
    private const int DefaultMaxStoredMessages = 100_000;
    private const int DefaultMaxStoredBytes = 1_073_741_824;
    private const int DefaultMaxInFlightMessages = 1_000;
    private const int DefaultMaxInFlightBytes = 67_108_864;
    private const int DefaultMaxLeaseSeconds = 300;
    private const int DefaultRetryBaseMilliseconds = 1_000;
    private const int DefaultRetryMaxMilliseconds = 300_000;
    private const int DefaultRetryExponentialFactor = 2;

    /// <summary>Gets or sets the max attempts value.</summary>
    [Orleans.Id(0)]
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
    /// <summary>Gets or sets the max stored messages value.</summary>
    [Orleans.Id(1)]
    public long MaxStoredMessages { get; init; } = DefaultMaxStoredMessages;
    /// <summary>Gets or sets the max stored bytes value.</summary>
    [Orleans.Id(2)]
    public long MaxStoredBytes { get; init; } = DefaultMaxStoredBytes;
    /// <summary>Gets or sets the max in flight messages value.</summary>
    [Orleans.Id(3)]
    public int MaxInFlightMessages { get; init; } = DefaultMaxInFlightMessages;
    /// <summary>Gets or sets the max in flight bytes value.</summary>
    [Orleans.Id(4)]
    public long MaxInFlightBytes { get; init; } = DefaultMaxInFlightBytes;
    /// <summary>Gets or sets the max lease seconds value.</summary>
    [Orleans.Id(5)]
    public int MaxLeaseSeconds { get; init; } = DefaultMaxLeaseSeconds;
    /// <summary>Gets or sets the retry base milliseconds value.</summary>
    [Orleans.Id(6)]
    public int RetryBaseMilliseconds { get; init; } = DefaultRetryBaseMilliseconds;
    /// <summary>Gets or sets the retry max milliseconds value.</summary>
    [Orleans.Id(7)]
    public int RetryMaxMilliseconds { get; init; } = DefaultRetryMaxMilliseconds;
    /// <summary>Gets the optional dead-letter message sublimit of the stored grant.</summary>
    [Orleans.Id(8)]
    public long? MaxDeadLetterMessages
    {
        get; init;
    }
    /// <summary>Gets the optional dead-letter byte sublimit of the stored grant.</summary>
    [Orleans.Id(9)]
    public long? MaxDeadLetterBytes
    {
        get; init;
    }
    /// <summary>Gets the canonical message ordering profile.</summary>
    [Orleans.Id(10)]
    public QueueOrderingProfile OrderingProfile
    {
        get; init;
    }
    /// <summary>Gets whether a parked strict head blocks its successors.</summary>
    [Orleans.Id(11)]
    public QueueParkedHeadPolicy ParkedHeadPolicy
    {
        get; init;
    }
    /// <summary>Gets the recorded retry jitter selection profile.</summary>
    [Orleans.Id(12)]
    public QueueRetryJitter RetryJitter
    {
        get; init;
    }
    /// <summary>Gets the saturating retry exponential factor.</summary>
    [Orleans.Id(13)]
    public int RetryExponentialFactor { get; init; } = DefaultRetryExponentialFactor;
}
