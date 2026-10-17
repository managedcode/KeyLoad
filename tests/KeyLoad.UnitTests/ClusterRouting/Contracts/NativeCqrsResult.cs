namespace KeyLoad.UnitTests.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCqrsProtocol.ResultAlias)]
internal sealed record NativeCqrsResult(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] NativeCqrsLeafObservation? Observation);
