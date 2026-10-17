namespace KeyLoad;

/// <summary>Returns bounded admin generation metadata and actual child evidence, without a parent commit token.</summary>
/// <param name="CommandId">Original stable maintenance correlation identity.</param>
/// <param name="Consumer">Original persisted consumer.</param>
/// <param name="IndexGeneration">Exact consumer generation.</param>
/// <param name="Phase">Observed final phase.</param>
/// <param name="Source">Exact admitted canonical upper source cut when a generation exists.</param>
/// <param name="Count">Actual retained native vector count.</param>
/// <param name="IndexSha256">Checksum of the actual immutable native index file.</param>
/// <param name="Checkpoint">Actual canonical child checkpoint result when one was returned.</param>
[Orleans.GenerateSerializer, Orleans.Alias(AnnMaintenanceAliases.Result)]
public sealed record AnnMaintenanceResult(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] long IndexGeneration,
    [property: Orleans.Id(3)] AnnMaintenancePhase Phase,
    [property: Orleans.Id(4)] AnnSourceCut? Source,
    [property: Orleans.Id(5)] int Count,
    [property: Orleans.Id(6)] string? IndexSha256,
    [property: Orleans.Id(7)] ProjectionBatchResult? Checkpoint);
