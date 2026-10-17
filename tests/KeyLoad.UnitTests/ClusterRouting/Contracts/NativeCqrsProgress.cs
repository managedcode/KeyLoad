namespace KeyLoad.UnitTests.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCqrsProtocol.ProgressAlias)]
internal sealed record NativeCqrsProgress(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string Stage);
