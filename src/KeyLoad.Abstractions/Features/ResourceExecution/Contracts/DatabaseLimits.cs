
namespace KeyLoad;

/// <summary>Defines server-side limits applied to database operations.</summary>
[ConfigurationOptions]
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DatabaseLimits)]
public sealed record DatabaseLimits
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:DatabaseLimits";
    /// <summary>Safe startup rejection for invalid operation budgets.</summary>
    public const string ValidationMessage = "Database execution limits must be positive; reserved outbox budgets cannot be negative.";
    private const int MinimumExecutionBudget = 1;
    private const int NoReservedBudget = 0;
    private const int DefaultMaxDocumentBytes = 1_048_576;
    private const int DefaultMaxJsonDepth = 32;
    private const int DefaultMaxBatchMutations = 256;
    private const int DefaultMaxBatchBytes = 8_388_608;
    private const int DefaultMaxScanRecords = 10_000;
    private const int DefaultMaxResults = 1_000;
    private const int DefaultWriterQueueCapacity = 256;
    private const int DefaultMaxConcurrentQueries = 16;
    private const int DefaultMaxQueryBytes = 65_536;
    private const int DefaultMaxQueryDepth = 32;
    private const int DefaultMaxQueryTokens = 2_048;
    private const int DefaultQueryDeadlineSeconds = 30;
    private const long DefaultMaxQueryReadBytes = 67_108_864;
    private const long DefaultMaxSearchTextTokens = 1_048_576;
    private const long DefaultMaxOutboxRecords = 100_000;
    private const long DefaultMaxOutboxBytes = 1_073_741_824;
    private const long DefaultReservedOutboxRecords = 16_384;
    private const long DefaultReservedOutboxBytes = 2_147_483_648;
    private const int DefaultMaxProjectionConsumers = 64;
    private const int DefaultMaxProjectionBatchBytes = 16_777_216;

    /// <summary>Gets or sets the max document bytes value.</summary>
    [Orleans.Id(0)]
    public int MaxDocumentBytes { get; init; } = DefaultMaxDocumentBytes;
    /// <summary>Gets or sets the max json depth value.</summary>
    [Orleans.Id(1)]
    public int MaxJsonDepth { get; init; } = DefaultMaxJsonDepth;
    /// <summary>Gets or sets the max batch mutations value.</summary>
    [Orleans.Id(2)]
    public int MaxBatchMutations { get; init; } = DefaultMaxBatchMutations;
    /// <summary>Gets or sets the max batch bytes value.</summary>
    [Orleans.Id(3)]
    public int MaxBatchBytes { get; init; } = DefaultMaxBatchBytes;
    /// <summary>Gets or sets the max scan records value.</summary>
    [Orleans.Id(4)]
    public int MaxScanRecords { get; init; } = DefaultMaxScanRecords;
    /// <summary>Gets or sets the max results value.</summary>
    [Orleans.Id(5)]
    public int MaxResults { get; init; } = DefaultMaxResults;
    /// <summary>Gets or sets the writer queue capacity value.</summary>
    [Orleans.Id(6)]
    public int WriterQueueCapacity { get; init; } = DefaultWriterQueueCapacity;
    /// <summary>Gets or sets the max concurrent queries value.</summary>
    [Orleans.Id(7)]
    public int MaxConcurrentQueries { get; init; } = DefaultMaxConcurrentQueries;
    /// <summary>Gets or sets the max query bytes value.</summary>
    [Orleans.Id(8)]
    public int MaxQueryBytes { get; init; } = DefaultMaxQueryBytes;
    /// <summary>Gets or sets the max query depth value.</summary>
    [Orleans.Id(9)]
    public int MaxQueryDepth { get; init; } = DefaultMaxQueryDepth;
    /// <summary>Gets or sets the max query tokens value.</summary>
    [Orleans.Id(10)]
    public int MaxQueryTokens { get; init; } = DefaultMaxQueryTokens;
    /// <summary>Gets or sets the query deadline seconds value.</summary>
    [Orleans.Id(11)]
    public int QueryDeadlineSeconds { get; init; } = DefaultQueryDeadlineSeconds;
    /// <summary>Gets or sets the max query read bytes value.</summary>
    [Orleans.Id(12)]
    public long MaxQueryReadBytes { get; init; } = DefaultMaxQueryReadBytes;
    /// <summary>Gets or sets the max search text tokens value.</summary>
    [Orleans.Id(13)]
    public long MaxSearchTextTokens { get; init; } = DefaultMaxSearchTextTokens;
    /// <summary>Gets or sets the max outbox records value.</summary>
    [Orleans.Id(14)]
    public long MaxOutboxRecords { get; init; } = DefaultMaxOutboxRecords;
    /// <summary>Gets or sets the max outbox bytes value.</summary>
    [Orleans.Id(15)]
    public long MaxOutboxBytes { get; init; } = DefaultMaxOutboxBytes;
    /// <summary>Gets or sets the reserved outbox records value.</summary>
    [Orleans.Id(16)]
    public long ReservedOutboxRecords { get; init; } = DefaultReservedOutboxRecords;
    /// <summary>Gets or sets the reserved outbox bytes value.</summary>
    [Orleans.Id(17)]
    public long ReservedOutboxBytes { get; init; } = DefaultReservedOutboxBytes;
    /// <summary>Gets or sets the max projection consumers value.</summary>
    [Orleans.Id(18)]
    public int MaxProjectionConsumers { get; init; } = DefaultMaxProjectionConsumers;
    /// <summary>Gets or sets the max projection batch bytes value.</summary>
    [Orleans.Id(19)]
    public int MaxProjectionBatchBytes { get; init; } = DefaultMaxProjectionBatchBytes;

    /// <summary>Checks that execution budgets are positive and optional reserved budgets are nonnegative.</summary>
    /// <returns>Whether the configured budgets are valid.</returns>
    public bool IsValid() => MaxDocumentBytes >= MinimumExecutionBudget
        && MaxJsonDepth >= MinimumExecutionBudget
        && MaxBatchMutations >= MinimumExecutionBudget
        && MaxBatchBytes >= MinimumExecutionBudget
        && MaxScanRecords >= MinimumExecutionBudget
        && MaxResults >= MinimumExecutionBudget
        && WriterQueueCapacity >= MinimumExecutionBudget
        && MaxConcurrentQueries >= MinimumExecutionBudget
        && MaxQueryBytes >= MinimumExecutionBudget
        && MaxQueryDepth >= MinimumExecutionBudget
        && MaxQueryTokens >= MinimumExecutionBudget
        && QueryDeadlineSeconds >= MinimumExecutionBudget
        && MaxQueryReadBytes >= MinimumExecutionBudget
        && MaxSearchTextTokens >= MinimumExecutionBudget
        && MaxOutboxRecords >= MinimumExecutionBudget
        && MaxOutboxBytes >= MinimumExecutionBudget
        && ReservedOutboxRecords >= NoReservedBudget
        && ReservedOutboxBytes >= NoReservedBudget
        && MaxProjectionConsumers >= MinimumExecutionBudget
        && MaxProjectionBatchBytes >= MinimumExecutionBudget;

    /// <summary>Rejects invalid budgets before database operations are admitted.</summary>
    /// <exception cref="InvalidOperationException">A configured operation budget is invalid.</exception>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
