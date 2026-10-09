namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ConnectionControlProtocol.Alias)]
internal sealed record GrainConnectionCloseControl(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] Guid ConnectionId,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt);
