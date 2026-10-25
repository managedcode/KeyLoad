namespace KeyLoad.FunctionalTests.OrleansExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(CqrsProtocol.ResultAlias)]
internal sealed record CqrsResult(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] CqrsLeafObservation? Observation);
