namespace KeyLoad.Orleans;

/// <summary>Central bounds for native saga wake scheduling and recovery.</summary>
[ConfigurationOptions]
public sealed record NativeDurableJobOptions
{
    /// <summary>The server-owned configuration section.</summary>
    public const string SectionName = "KeyLoad:DurableJobs";
    /// <summary>The safe validation error.</summary>
    public const string ValidationMessage = "Native durable job settings exceed their execution and recovery bounds.";
    private const int ConcurrencyLimit = 8;
    private const int RetryLimit = 3;
    private const int AdoptionLimit = 3;
    private const int CatalogLimit = 32;
    private const int DefaultCheckMilliseconds = 1_000;
    private const int DefaultPollMilliseconds = 100;
    private const int MaximumPolicySeconds = 60;

    /// <summary>Gets the maximum native jobs running on one silo.</summary>
    public int MaximumConcurrentJobs { get; init; } = ConcurrencyLimit;
    /// <summary>Gets the maximum attempts, including the initial attempt.</summary>
    public int MaximumAttempts { get; init; } = RetryLimit;
    /// <summary>Gets the delay before another transient failed attempt.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(DefaultCheckMilliseconds);
    /// <summary>Gets the bounded native shard time bucket.</summary>
    public TimeSpan ShardDuration { get; init; } = TimeSpan.FromSeconds(MaximumPolicySeconds);
    /// <summary>Gets the periodic discovery cadence.</summary>
    public TimeSpan ShardCheckInterval { get; init; } = TimeSpan.FromMilliseconds(DefaultCheckMilliseconds);
    /// <summary>Gets the handler completion polling cadence.</summary>
    public TimeSpan JobStatusPollInterval { get; init; } = TimeSpan.FromMilliseconds(DefaultPollMilliseconds);
    /// <summary>Gets the bounded overload recheck delay.</summary>
    public TimeSpan OverloadBackoffDelay { get; init; } = TimeSpan.FromMilliseconds(DefaultCheckMilliseconds);
    /// <summary>Gets the maximum crash adoptions before native poisoning.</summary>
    public int MaximumAdoptedCount { get; init; } = AdoptionLimit;
    /// <summary>Gets the initial orphan claim capacity.</summary>
    public int InitialClaimBudget { get; init; } = 1;
    /// <summary>Gets the bounded catalog claim capacity after ramp-up.</summary>
    public int MaximumClaimBudget { get; init; } = CatalogLimit;
    /// <summary>Gets the bounded orphan claim ramp-up duration.</summary>
    public TimeSpan ClaimRampUpDuration { get; init; } = TimeSpan.FromMilliseconds(DefaultCheckMilliseconds);
    /// <summary>Gets the cadence for waiting on all configured startup voters.</summary>
    public TimeSpan BootstrapPollInterval { get; init; } = TimeSpan.FromMilliseconds(DefaultPollMilliseconds);

    /// <summary>Checks native execution and discovery settings before any ownership begins.</summary>
    /// <returns>Whether the settings are within the accepted bounds.</returns>
    public bool IsValid()
        => MaximumConcurrentJobs is > 0 and <= ConcurrencyLimit
            && MaximumAttempts is > 0 and <= RetryLimit
            && MaximumAdoptedCount is >= 0 and <= AdoptionLimit
            && InitialClaimBudget is > 0 and <= CatalogLimit
            && MaximumClaimBudget >= InitialClaimBudget && MaximumClaimBudget <= CatalogLimit
            && PositiveBounded(RetryDelay) && PositiveBounded(ShardDuration)
            && PositiveBounded(ShardCheckInterval) && PositiveBounded(JobStatusPollInterval)
            && PositiveBounded(OverloadBackoffDelay) && PositiveBounded(ClaimRampUpDuration)
            && PositiveBounded(BootstrapPollInterval);

    private static bool PositiveBounded(TimeSpan duration)
        => duration >= TimeSpan.FromMilliseconds(1) && duration <= TimeSpan.FromSeconds(MaximumPolicySeconds);
}
