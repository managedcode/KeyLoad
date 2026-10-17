namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionObservationProtocol.RequestAlias)]
internal sealed record AnnProjectionObservationRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] AnnProjectionObservationKind Kind,
    [property: global::Orleans.Id(2)] AnnMaintenanceRequest OriginalRequest,
    [property: global::Orleans.Id(3)] long MaximumReadBytes,
    [property: global::Orleans.Id(4)] int MaximumRecords,
    [property: global::Orleans.Id(5)] WaitForAnnIndexRequest? Wait,
    [property: global::Orleans.Id(6)] ApproximateSearchRequest? Approximate);
