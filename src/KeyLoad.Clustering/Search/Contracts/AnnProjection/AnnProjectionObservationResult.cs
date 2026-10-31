namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionObservationProtocol.ResultAlias)]
internal sealed record AnnProjectionObservationResult(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid NodeId,
    [property: global::Orleans.Id(2)] long ReadGeneration,
    [property: global::Orleans.Id(3)] WaitForAnnIndexResult? Wait,
    [property: global::Orleans.Id(6)] AnnSearchPage? Approximate);
