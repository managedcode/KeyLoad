using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class NativeCanonicalModelCutDiagnosticEndpoints
{
    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Services.GetRequiredService<IOptions<NodeOptions>>().Value.RequestCqrsProbe.Enabled)
        {
            return;
        }
        app.MapPost(NativeCanonicalModelCutDiagnosticProtocol.Path, ReadAsync);
        NativeRetireCutEndpoints.Map(app);
        NativeCommandLifetimeCutEndpoints.Map(app);
        Features.ResourceExecution.NativeResourceOwnerInventoryDiagnosticEndpoints.Map(app);
        Features.ResourceExecution.NativeResourceRetirementDiagnosticEndpoints.Map(app);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, PartitionHostApplicationOwner owner, OrleansNode node, TimeProvider clock)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord principal)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var partition = owner.Borrow();
        principal = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var observer = node.CanonicalCutObserver ?? throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        using var lease = observer.OpenCanonicalCut(context.RequestAborted);
        var request = await NativeCanonicalModelCutBody.ReadAsync(context.Request, lease.Token).ConfigureAwait(false);
        lease.Bind(request);
        var result = NativeCanonicalModelCutDiagnosticRead.Capture(partition, node, clock, principal, request, observer, lease);
        context.Response.Headers[NativeCanonicalModelCutDiagnosticProtocol.SignatureHeader] = result.Signature;
        return Results.Bytes(result.Payload, NativeCanonicalModelCutDiagnosticProtocol.ContentType);
    }
}
