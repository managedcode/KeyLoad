using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Dispatches authorized backup reads to their original node-local physical owners.</summary>
internal static class PhysicalOwnerReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.ClusterBackupOwner;

    internal static async Task<object?> ExecuteAsync(IServiceProvider services, TimeProvider runtimeClock,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.ClusterBackupOwner)
        {
            return await ClusterBackupOwnerReadExecution.ExecuteAsync(services.GetRequiredService<INodeAdministration>(), principal, request.Payload,
                services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseLimits>>(), runtimeClock,
                request.Envelope.ExpiresAt, cancellationToken).ConfigureAwait(true);
        }
        throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
    }
}
