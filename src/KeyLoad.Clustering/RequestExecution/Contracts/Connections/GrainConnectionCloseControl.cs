namespace KeyLoad.Clustering;

/// <summary>The expiring control that closes one server-owned connection within one database incarnation.</summary>
/// <param name="Purpose">The fixed close purpose.</param>
/// <param name="Incarnation">The database incarnation the control is scoped to.</param>
/// <param name="ConnectionId">The server-issued connection identity.</param>
/// <param name="ExpiresAt">The instant after which the control is refused.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ConnectionControlProtocol.Alias)]
public sealed record GrainConnectionCloseControl(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] Guid ConnectionId,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt);
