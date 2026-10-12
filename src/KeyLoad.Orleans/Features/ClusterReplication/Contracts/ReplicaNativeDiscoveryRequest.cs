namespace KeyLoad.Orleans;

/// <summary>Authenticates a bounded metadata read between actual current native silo generations.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaNativeDiscoveryProtocol.RequestAlias)]
public sealed record ReplicaNativeDiscoveryRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string CallerVoter,
    [property: global::Orleans.Id(2)] string CallerSiloAddress,
    [property: global::Orleans.Id(3)] string DestinationVoter,
    [property: global::Orleans.Id(4)] string DestinationSiloAddress,
    [property: global::Orleans.Id(5)] Guid Nonce,
    [property: global::Orleans.Id(6)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(7)] string Signature);
