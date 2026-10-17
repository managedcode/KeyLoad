namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnOriginalOperation.SerializerAlias)]
internal sealed record NativeAnnOriginalOperation(
    [property: global::Orleans.Id(0)] NativeAnnOwnedKey Key,
    [property: global::Orleans.Id(1)] string PrincipalId,
    [property: global::Orleans.Id(2)] long PolicyEpoch,
    [property: global::Orleans.Id(3)] AnnMaintenanceRequest? PendingRequest,
    [property: global::Orleans.Id(4)] bool PendingStarted,
    [property: global::Orleans.Id(5)] AnnMaintenanceRequest? LastRequest,
    [property: global::Orleans.Id(6)] AnnMaintenanceResult? LastResult,
    [property: global::Orleans.Id(7)] bool Released,
    [property: global::Orleans.Id(8)] AnnMaintenanceRequest InitialRequest,
    [property: global::Orleans.Id(9)] bool InitialSettled,
    [property: global::Orleans.Id(10)] CommitProjectionBatchRequest? InitialSettlementIntent,
    [property: global::Orleans.Id(11)] NativeAnnRetiredOriginalIdentity[] RetiredOriginals)
{
    internal const string SerializerAlias = "keyload.server.ann-original-operation.v1";
}
