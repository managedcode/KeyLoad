using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Dispatches authorized backup and movement reads to their original node-local physical owners.</summary>
internal static class PhysicalOwnerReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.ClusterBackupOwner or GrainReadKind.PartitionMovementTransferData
            or GrainReadKind.PartitionMovementCapture;

    internal static async Task<object?> ExecuteAsync(IServiceProvider services, TimeProvider runtimeClock,
        GrainRequestCodec codec, IGrainContext context, PrincipalRecord principal, DecodedGrainRequest request,
        CancellationToken cancellationToken)
    {
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.ClusterBackupOwner)
        {
            return await ClusterBackupOwnerReadExecution.ExecuteAsync(services.GetRequiredService<INodeAdministration>(), principal, request.Payload,
                services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseLimits>>(), runtimeClock,
                request.Envelope.ExpiresAt, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionMovementTransferData)
        {
            var execution = new PartitionMovementTransferDataObservedExecution(
                services.GetRequiredService<INativePartitionMovementTransferRead>(),
                services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseLimits>>(), runtimeClock, codec);
            return await execution.ExecuteAsync(principal, request, context,
                cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionMovementCapture)
        {
            return await PartitionMovementCaptureExecution.ExecuteAsync(
                services.GetRequiredService<Func<INativePartitionMovementCapture>>()(), principal,
                GrainNativePayload.Read<PartitionMovementCaptureCapability>(request.Payload),
                cancellationToken).ConfigureAwait(true);
        }
        throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
    }
}
