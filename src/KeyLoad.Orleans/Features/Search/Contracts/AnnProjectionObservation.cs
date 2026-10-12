namespace KeyLoad.Orleans;

[global::Orleans.Alias(AnnProjectionObservationProtocol.KindAlias)]
internal enum AnnProjectionObservationKind
{
    Identity = 0,
    Wait = 1,
    Approximate = 2
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionObservationProtocol.RequestAlias)]
internal sealed record AnnProjectionObservationRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] AnnProjectionObservationKind Kind,
    [property: global::Orleans.Id(2)] AnnMaintenanceRequest OriginalRequest,
    [property: global::Orleans.Id(3)] long MaximumReadBytes,
    [property: global::Orleans.Id(4)] int MaximumRecords,
    [property: global::Orleans.Id(5)] WaitForAnnIndexRequest? Wait,
    [property: global::Orleans.Id(6)] ApproximateSearchRequest? Approximate);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionObservationProtocol.ResultAlias)]
internal sealed record AnnProjectionObservationResult(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid NodeId,
    [property: global::Orleans.Id(2)] long ReadGeneration,
    [property: global::Orleans.Id(3)] WaitForAnnIndexResult? Wait,
    [property: global::Orleans.Id(4)] long AcceptedReadBytes,
    [property: global::Orleans.Id(5)] int ExaminedRecords,
    [property: global::Orleans.Id(6)] AnnSearchPage? Approximate);
