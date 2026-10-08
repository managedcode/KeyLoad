namespace KeyLoad;

/// <summary>Names actual bounded work phases without private document data.</summary>
public enum TextIndexMaintenancePhase
{
    /// <summary>Admit an exact persisted consumer and source owner.</summary>
    Configure,
    /// <summary>Capture a complete bounded canonical seed or recovery authority.</summary>
    Capture,
    /// <summary>Persist original native page work before changing postings.</summary>
    Intent,
    /// <summary>Apply the actual native index changes.</summary>
    NativeIndex,
    /// <summary>Join native handles and publish verified complete inventory.</summary>
    Publish,
    /// <summary>Observe the original canonical checkpoint child receipt.</summary>
    Checkpoint,
    /// <summary>Fence and release the exact obsolete consumer generation.</summary>
    Release,
    /// <summary>All bounded work and its actual canonical receipts completed.</summary>
    Completed
}

/// <summary>Describes the observed canonical source cut; this is not a bearer token.</summary>
/// <param name="NodeId">Actual node-local source owner.</param>
/// <param name="Incarnation">Actual current source incarnation.</param>
/// <param name="Position">Canonical local position.</param>
/// <param name="AppliedPosition">Replicated applied authority at the same cut.</param>
/// <param name="ThroughSequence">Exact canonical source upper.</param>
/// <param name="ReadGeneration">Canonical replacement generation.</param>
/// <param name="SchemaVersion">Current persisted collection schema version.</param>
/// <param name="PolicyEpoch">Current persisted administrator policy epoch.</param>
/// <param name="ResourceSha256">Exact native persisted resource identity digest.</param>
[Orleans.GenerateSerializer, Orleans.Alias(TextIndexMaintenanceAliases.Cut)]
public sealed record TextIndexSourceCut(
    [property: Orleans.Id(0)] Guid NodeId,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Position,
    [property: Orleans.Id(3)] long AppliedPosition,
    [property: Orleans.Id(4)] long ThroughSequence,
    [property: Orleans.Id(5)] long ReadGeneration,
    [property: Orleans.Id(6)] long SchemaVersion,
    [property: Orleans.Id(7)] long PolicyEpoch,
    [property: Orleans.Id(8)] string ResourceSha256);

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
