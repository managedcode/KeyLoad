
namespace KeyLoad;

/// <summary>Defines server-side limits applied to database operations.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DatabaseLimits)]
public sealed record DatabaseLimits
{
    /// <summary>Gets or sets the max document bytes value.</summary>
    [Orleans.Id(0)]
    public int MaxDocumentBytes { get; init; } = 1_048_576;
    /// <summary>Gets or sets the max json depth value.</summary>
    [Orleans.Id(1)]
    public int MaxJsonDepth { get; init; } = 32;
    /// <summary>Gets or sets the max batch mutations value.</summary>
    [Orleans.Id(2)]
    public int MaxBatchMutations { get; init; } = 256;
    /// <summary>Gets or sets the max batch bytes value.</summary>
    [Orleans.Id(3)]
    public int MaxBatchBytes { get; init; } = 8_388_608;
    /// <summary>Gets or sets the max scan records value.</summary>
    [Orleans.Id(4)]
    public int MaxScanRecords { get; init; } = 10_000;
    /// <summary>Gets or sets the max results value.</summary>
    [Orleans.Id(5)]
    public int MaxResults { get; init; } = 1_000;
    /// <summary>Gets or sets the writer queue capacity value.</summary>
    [Orleans.Id(6)]
    public int WriterQueueCapacity { get; init; } = 256;
    /// <summary>Gets or sets the max concurrent queries value.</summary>
    [Orleans.Id(7)]
    public int MaxConcurrentQueries { get; init; } = 16;
    /// <summary>Gets or sets the max query bytes value.</summary>
    [Orleans.Id(8)]
    public int MaxQueryBytes { get; init; } = 65_536;
    /// <summary>Gets or sets the max query depth value.</summary>
    [Orleans.Id(9)]
    public int MaxQueryDepth { get; init; } = 32;
    /// <summary>Gets or sets the max query tokens value.</summary>
    [Orleans.Id(10)]
    public int MaxQueryTokens { get; init; } = 2_048;
    /// <summary>Gets or sets the query deadline seconds value.</summary>
    [Orleans.Id(11)]
    public int QueryDeadlineSeconds { get; init; } = 30;
    /// <summary>Gets or sets the max query read bytes value.</summary>
    [Orleans.Id(12)]
    public long MaxQueryReadBytes { get; init; } = 67_108_864;
    /// <summary>Gets or sets the max search text tokens value.</summary>
    [Orleans.Id(13)]
    public long MaxSearchTextTokens { get; init; } = 1_048_576;
    /// <summary>Gets or sets the max outbox records value.</summary>
    [Orleans.Id(14)]
    public long MaxOutboxRecords { get; init; } = 100_000;
    /// <summary>Gets or sets the max outbox bytes value.</summary>
    [Orleans.Id(15)]
    public long MaxOutboxBytes { get; init; } = 1_073_741_824;
    /// <summary>Gets or sets the reserved outbox records value.</summary>
    [Orleans.Id(16)]
    public long ReservedOutboxRecords { get; init; } = 16_384;
    /// <summary>Gets or sets the reserved outbox bytes value.</summary>
    [Orleans.Id(17)]
    public long ReservedOutboxBytes { get; init; } = 2_147_483_648;
    /// <summary>Gets or sets the max projection consumers value.</summary>
    [Orleans.Id(18)]
    public int MaxProjectionConsumers { get; init; } = 64;
    /// <summary>Gets or sets the max projection batch bytes value.</summary>
    [Orleans.Id(19)]
    public int MaxProjectionBatchBytes { get; init; } = 16_777_216;
}
