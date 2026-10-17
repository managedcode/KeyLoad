using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class NativeCommandLifetimeCutEndpoints
{
    internal static void Map(WebApplication app) => app.MapPost(NativeCommandLifetimeCutWire.Path, ReadAsync);

    private static async Task<IResult> ReadAsync(HttpContext context, PartitionHostApplicationOwner owner,
        OrleansNode node, TimeProvider clock, IOptions<DatabaseLimits> limits)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord supplied)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var partition = owner.Borrow();
        var principal = GrainRequestAuthority.Reload(partition.Database, supplied.Id, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var observer = node.CanonicalCutObserver ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        using var lease = observer.OpenCanonicalCut(context.RequestAborted);
        var request = await NativeCommandLifetimeCutBody.ReadAsync(context.Request, lease.Token).ConfigureAwait(false);
        lease.Bind(request);
        await partition.Coordinator.ReadBarrierAsync(lease.Token).ConfigureAwait(false);
        var authentication = node.Authentication ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        var discovery = node.Discovery ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        var actual = discovery.Read();
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        var witness = NativeCommandLifetimeCutCapture.Read(partition.Database, actual, request, limits, lease.Token);
        var bytes = NativeCommandLifetimeCutCodec.Encode(witness, lease.Token);
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        if (!ReferenceEquals(observer, node.CanonicalCutObserver) || !ReferenceEquals(authentication, node.Authentication)
            || !ReferenceEquals(discovery, node.Discovery) || discovery.Read() != actual)
        {
            throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        }
        var current = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock);
        GrainRequestAuthority.RequireAdministrator(current);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, NativeCanonicalModelCutDiagnosticProtocol.Unavailable);
        }
        lease.RequireCurrent();
        context.Response.Headers[NativeCommandLifetimeCutWire.SignatureHeader] = authentication.SignDiscovery(
            NativeCommandLifetimeCutWire.Frame(bytes), request.Nonce);
        return Results.Bytes(bytes, NativeCanonicalModelCutDiagnosticProtocol.ContentType);
    }
}
