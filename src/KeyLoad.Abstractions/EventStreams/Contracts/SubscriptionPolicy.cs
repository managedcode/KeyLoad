namespace KeyLoad;

/// <summary>Configures retry and lease behavior for a subscription.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionPolicy)]
public sealed record SubscriptionPolicy
{
    private const int DefaultMaxWindow = 1_024;
    private const int DefaultMaxLeaseSeconds = 300;
    private const int DefaultMaxAttempts = 5;
    private const int DefaultRetryBaseMilliseconds = 1_000;
    private const int DefaultRetryMaxMilliseconds = 300_000;

    /// <summary>Gets or sets the maximum number of outstanding deliveries.</summary>
    [Orleans.Id(0)]
    public int MaxWindow { get; init; } = DefaultMaxWindow;
    /// <summary>Gets or sets the maximum lease duration in seconds.</summary>
    [Orleans.Id(1)]
    public int MaxLeaseSeconds { get; init; } = DefaultMaxLeaseSeconds;
    /// <summary>Gets or sets the maximum delivery attempts.</summary>
    [Orleans.Id(2)]
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
    /// <summary>Gets or sets the base delay between retries in milliseconds.</summary>
    [Orleans.Id(3)]
    public int RetryBaseMilliseconds { get; init; } = DefaultRetryBaseMilliseconds;
    /// <summary>Gets or sets the maximum delay between retries in milliseconds.</summary>
    [Orleans.Id(4)]
    public int RetryMaxMilliseconds { get; init; } = DefaultRetryMaxMilliseconds;
}
