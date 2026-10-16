using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeRetireCutEndpoints
{
    internal static void Map(WebApplication app) => app.MapPost(NativeRetireCutWire.Path, ReadAsync);

    private static async Task<IResult> ReadAsync(HttpContext context, PartitionHostApplicationOwner owner,
        OrleansNode node, TimeProvider clock)
    {
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord supplied)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var partition = owner.Borrow();
        var principal = GrainRequestAuthority.Reload(partition.Database, supplied.Id, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var observer = node.CanonicalCutObserver ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        using var lease = observer.OpenCanonicalCut(context.RequestAborted);
        var request = await NativeRetireCutBody.ReadAsync(context.Request, lease.Token).ConfigureAwait(false);
        lease.Bind(request);
        await partition.Coordinator.ReadBarrierAsync(lease.Token).ConfigureAwait(false);
        var authentication = node.Authentication ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        var discovery = node.Discovery ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        var actual = discovery.Read();
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        var before = await partition.Consensus.StateAsync(lease.Token).ConfigureAwait(false);
        var witness = NativeRetireCutCapture.Read(partition, actual, request, before, observer, lease.Token);
        var bytes = NativeSerialization.Serialize(witness);
        if (bytes.Length > partition.Database.Limits.MaxQueryReadBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        var after = await partition.Consensus.StateAsync(lease.Token).ConfigureAwait(false);
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        if (before.LastIndex != after.LastIndex || before.CommittedIndex != after.CommittedIndex
            || !ReferenceEquals(observer, node.CanonicalCutObserver) || !ReferenceEquals(authentication, node.Authentication)
            || !ReferenceEquals(discovery, node.Discovery) || discovery.Read() != actual)
        {
            throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        }
        var current = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock);
        GrainRequestAuthority.RequireAdministrator(current);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        }
        lease.RequireCurrent();
        context.Response.Headers[NativeRetireCutWire.SignatureHeader] = authentication.SignDiscovery(
            NativeRetireCutWire.SignatureFrame(bytes), request.Nonce);
        return Results.Bytes(bytes, NativeCanonicalModelCutDiagnosticProtocol.ContentType);
    }
}
