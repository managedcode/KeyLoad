using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeResourceOwnerInventoryDiagnosticEndpoints
{
    internal static void Map(WebApplication app)
        => app.MapPost(NativeResourceOwnerInventoryDiagnosticProtocol.Path, ReadAsync);

    private static async Task<IResult> ReadAsync(HttpContext context, PartitionHostApplicationOwner owner,
        OrleansNode node, ResourceOwnerInventoryNativeClient client, IOptions<NodeOptions> nodeOptions,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (!nodeOptions.Value.RequestCqrsProbe.Enabled)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord supplied)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var partition = owner.Borrow();
        var principal = GrainRequestAuthority.Reload(partition.Database, supplied.Id, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var observer = node.CanonicalCutObserver
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
        var failures = new List<Exception>();
        var result = (Payload: Array.Empty<byte>(), Signature: string.Empty);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var lease = observer.OpenCanonicalCut(context.RequestAborted);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var input = await NativeResourceOwnerInventoryDiagnosticBody.ReadAsync(context.Request, limits, lease.Token).ConfigureAwait(false);
                result = await NativeResourceOwnerInventoryDiagnosticCapture.ReadAsync(partition, node, principal,
                    input, client, nodeOptions, limits, routing, clock, observer, lease.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        context.RequestAborted.ThrowIfCancellationRequested();
        context.Response.Headers[NativeResourceOwnerInventoryDiagnosticProtocol.SignatureHeader] = result.Signature;
        return Results.Bytes(result.Payload, NativeResourceOwnerInventoryDiagnosticProtocol.ContentType);
    }
}
