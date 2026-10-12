using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal static class NativeCanonicalModelCutDiagnosticRead
{
    internal static (byte[] Payload, string Signature) Capture(PartitionHost partition, OrleansNode node,
        TimeProvider clock, PrincipalRecord supplied, NativeCanonicalModelCutRequest request,
        RequestCqrsProbeObserver observer, NativeCanonicalModelCutLease lease)
    {
        lease.RequireCurrent();
        var principal = GrainRequestAuthority.Reload(partition.Database, supplied.Id, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var discovery = node.Discovery;
        var authentication = node.Authentication;
        var state = discovery?.Read();
        if (discovery is null || state is null || authentication is null
            || !ReferenceEquals(observer, node.CanonicalCutObserver))
        { throw Unavailable(); }
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, state);
        var witness = NativeCanonicalModelCutCapture.Capture(partition.Database, state, request, lease.Token);
        var payload = NativeCanonicalModelCutCodec.Encode(witness, lease.Token);
        var signature = authentication.SignDiscovery(NativeCanonicalModelCutSignatureFrame.Create(payload), request.Nonce);
        lease.RequireCurrent();
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, state);
        if (!ReferenceEquals(observer, node.CanonicalCutObserver) || !ReferenceEquals(discovery, node.Discovery)
            || !ReferenceEquals(authentication, node.Authentication) || discovery.Read() != state)
        { throw Unavailable(); }
        var current = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock);
        GrainRequestAuthority.RequireAdministrator(current);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, NativeCanonicalModelCutDiagnosticProtocol.Unavailable); }
        lease.RequireCurrent();
        return (payload, signature);
    }

    internal static KeyLoadException Unavailable()
        => Errors.Fail(ErrorCode.OwnershipLost, NativeCanonicalModelCutDiagnosticProtocol.Unavailable);
}
