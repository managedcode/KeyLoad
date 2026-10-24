namespace KeyLoad.UnitTests.OrleansExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(CqrsProtocol.LeafObservationAlias)]
internal sealed record CqrsLeafObservation(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string CallerContext,
    [property: global::Orleans.Id(2)] string TargetInterface,
    [property: global::Orleans.Id(3)] string TargetMethod,
    [property: global::Orleans.Id(4)] string ObservedSource,
    [property: global::Orleans.Id(5)] string ObservedTarget,
    [property: global::Orleans.Id(6)] string ObservedSourceMethod,
    [property: global::Orleans.Id(7)] string ObservedTargetMethod,
    [property: global::Orleans.Id(8)] string OutgoingSourceId,
    [property: global::Orleans.Id(9)] string OutgoingTargetId,
    [property: global::Orleans.Id(10)] string IncomingSourceId,
    [property: global::Orleans.Id(11)] string IncomingTargetId,
    [property: global::Orleans.Id(12)] string LeafPrimaryKey);
