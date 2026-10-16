using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class ConnectionRemoteTransferReadCapabilities
{
    internal static bool HandlesPeer(GrainReadKind kind, GrainRequestEnvelope envelope)
        => kind == GrainReadKind.QueueTransferCoordination && envelope.Purpose == RemoteTransferPeerProtocol.GrainPurpose;

    internal static object? ExecutePeer(DatabaseEngine database, TimeProvider clock,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return GrainRemoteTransferRead.Execute(database, principal, request,
            database.OperationLimitsOptions, clock, cancellationToken, binding);
    }

    internal static Task<RemoteQueueTransferReceiptRead> TryReceiptAsync(IServiceProvider services,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return services.GetService<IRemoteDocumentReadRouter>() is IRemoteQueueTransferReceiptRouter transfers
            ? transfers.InspectTransferAsync(request.Envelope, principal,
                GrainNativePayload.Read<InspectQueueTransferReceiptRequest>(request.Payload), cancellationToken, binding)
            : Task.FromResult(new RemoteQueueTransferReceiptRead(false, null));
    }
}
