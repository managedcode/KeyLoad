namespace KeyLoad;

/// <summary>The complete immutable result of one canonical online index publication.</summary>
/// <param name="CommandId">CommandId within the original authorized maintenance request.</param>
/// <param name="Consumer">Consumer within the original authorized maintenance request.</param>
/// <param name="ConsumerGeneration">ConsumerGeneration within the original authorized maintenance request.</param>
/// <param name="BaseCut">BaseCut within the original authorized maintenance request.</param>
/// <param name="PublishedCut">PublishedCut within the original authorized maintenance request.</param>
/// <param name="IndexSha256">IndexSha256 within the original authorized maintenance request.</param>
/// <param name="TrackedRecords">TrackedRecords within the original authorized maintenance request.</param>
/// <param name="Checkpoint">Checkpoint within the original authorized maintenance request.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextIndexMaintenanceProtocol.ResultAlias)]
public sealed record OnlineTextIndexMaintenanceResult(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(2)] long ConsumerGeneration,
    [property: global::Orleans.Id(3)] TextIndexSourceCut BaseCut,
    [property: global::Orleans.Id(4)] TextIndexSourceCut PublishedCut,
    [property: global::Orleans.Id(5)] string IndexSha256,
    [property: global::Orleans.Id(6)] int TrackedRecords,
    [property: global::Orleans.Id(7)] ProjectionBatchResult Checkpoint);
