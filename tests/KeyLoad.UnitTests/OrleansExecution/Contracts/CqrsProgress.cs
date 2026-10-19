namespace KeyLoad.UnitTests.Features.OrleansExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(CqrsProtocol.ProgressAlias)]
internal sealed record CqrsProgress(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string Stage);
