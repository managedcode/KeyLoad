namespace KeyLoad.Clustering;

/// <summary>Retains the original execution ANN job identity, physical binding and settled result.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionJobProtocol.StateAlias)]
public sealed record AnnProjectionJobState(
    [property: global::Orleans.Id(0)] ProjectionMaintenanceAuthority Authority,
    [property: global::Orleans.Id(1)] AnnMaintenanceRequest InitialRequest,
    [property: global::Orleans.Id(2)] AnnMaintenanceRequest? PendingRequest,
    [property: global::Orleans.Id(3)] string? JobId,
    [property: global::Orleans.Id(4)] AnnMaintenanceResult? LastResult,
    [property: global::Orleans.Id(5)] ErrorCode? BlockedError,
    [property: global::Orleans.Id(6)] long RequiredThroughSequence,
    [property: global::Orleans.Id(7)] AnnMaintenanceRequest? LastRequest);
