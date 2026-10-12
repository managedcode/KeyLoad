namespace KeyLoad.Orleans;

// Borrowed only by the protected fixture's original discovery transport owner.
// The optional received hook injects a protected fixture fault into an actual native reply.
// Production leaves it null; all native authentication and membership checks still apply.
/// <summary>Observes the actual native request and authenticated reply under their original cancellation.</summary>
internal sealed record ReplicaDiscoveryProbe(
    Action<ReplicaNativeDiscoveryRequest, CancellationToken> BeforeSend,
    Action<string, Guid, ReplicaSiloDiscovery, CancellationToken> Verified,
    Func<ReplicaNativeDiscoveryRequest, ReplicaNativeDiscoveryReply, CancellationToken, ReplicaNativeDiscoveryReply>? Received = null,
    Func<ReplicaNativeDiscoveryRequest, CancellationToken, ValueTask>? BeforeSendAsync = null);
