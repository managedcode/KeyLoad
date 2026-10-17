namespace KeyLoad;

/// <summary>Returns admin metadata and actual canonical child evidence, without a parent atomic token.</summary>
/// <param name="CommandId">Original immutable maintenance parent identity.</param>
/// <param name="Consumer">Exact persisted consumer.</param>
/// <param name="IndexGeneration">Exact persisted generation.</param>
/// <param name="Phase">Actual final phase.</param>
/// <param name="Source">Admitted canonical source cut, when a generation exists.</param>
/// <param name="TrackedRecords">Actual bounded stable-ID map count, including revision tombstones.</param>
/// <param name="IndexSha256">Actual published complete native inventory identity.</param>
/// <param name="Checkpoint">Actual final canonical checkpoint child result, when returned.</param>
/// <param name="IndexedThroughSequence">Actual published prefix, distinct from the observed canonical upper; null after release.</param>
/// <param name="ReleasedConsumer">Actual immutable canonical release child result, when returned.</param>
[Orleans.GenerateSerializer, Orleans.Alias(TextIndexMaintenanceAliases.Result)]
public sealed record TextIndexMaintenanceResult(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] long IndexGeneration,
    [property: Orleans.Id(3)] TextIndexMaintenancePhase Phase,
    [property: Orleans.Id(4)] TextIndexSourceCut? Source,
    [property: Orleans.Id(5)] int TrackedRecords,
    [property: Orleans.Id(6)] string? IndexSha256,
    [property: Orleans.Id(7)] ProjectionBatchResult? Checkpoint,
    [property: Orleans.Id(8)] ProjectionConsumerInfo? ReleasedConsumer = null,
    [property: Orleans.Id(9), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    long? IndexedThroughSequence = null);
