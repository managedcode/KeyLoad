namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionSettlementClaims.ContractAlias)]
internal sealed record AnnProjectionSettlementClaims(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] string PrincipalId,
    [property: global::Orleans.Id(3)] long PolicyEpoch,
    [property: global::Orleans.Id(4)] Guid ConfigureCommandId,
    [property: global::Orleans.Id(5)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(6)] long IndexGeneration,
    [property: global::Orleans.Id(7)] Guid InitialCommandId,
    [property: global::Orleans.Id(8)] byte[] InitialRequestSha256,
    [property: global::Orleans.Id(9)] byte[] OriginalResultSha256,
    [property: global::Orleans.Id(10)] long Checkpoint,
    [property: global::Orleans.Id(11)] Guid NodeId,
    [property: global::Orleans.Id(12)] PhysicalShardRecord PhysicalPlacement,
    [property: global::Orleans.Id(13)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(14)] Guid SettlementCommandId)
{
    internal const string ContractAlias = "keyload.core.ann-projection-settlement-claims.v1";
    internal const string SettlementPurpose = "ann-initial-settlement";
}
