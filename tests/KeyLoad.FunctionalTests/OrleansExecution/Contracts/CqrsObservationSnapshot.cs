namespace KeyLoad.FunctionalTests.OrleansExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(CqrsProtocol.SnapshotAlias)]
internal sealed record CqrsObservationSnapshot(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] IReadOnlyList<string> Events,
    [property: global::Orleans.Id(2)] bool Settled,
    [property: global::Orleans.Id(3)] bool CancellationObserved,
    [property: global::Orleans.Id(4)] int SettlementCount,
    [property: global::Orleans.Id(5)] CqrsLeafObservation? Observation,
    [property: global::Orleans.Id(6)] bool HandlerOutcomeReturned,
    [property: global::Orleans.Id(7)] bool HandlerOutcomeSucceeded);
