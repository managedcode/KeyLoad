namespace KeyLoad.Orleans;

// Borrowed only by the protected fixture's original discovery transport owner.
// Neither callback supplies authority or retains request/response payloads.
internal sealed record ReplicaDiscoveryProbe(
    Action<HttpRequestMessage, CancellationToken> BeforeSend,
    Action<string, Guid, ReplicaSiloDiscovery, CancellationToken> Verified);
