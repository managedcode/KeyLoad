namespace KeyLoad.FunctionalTests.OrleansExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(CqrsProtocol.LeafObservationAlias)]
internal sealed record CqrsLeafObservation(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(2)] string TargetInterface,
    [property: global::Orleans.Id(3)] string TargetMethod,
    [property: global::Orleans.Id(12)] string LeafPrimaryKey);
