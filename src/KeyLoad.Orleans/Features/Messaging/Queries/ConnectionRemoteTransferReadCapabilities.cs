using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class ConnectionRemoteTransferReadCapabilities
{
    internal static bool HandlesPeer(GrainReadKind kind, GrainRequestEnvelope envelope)
        => kind == GrainReadKind.QueueTransferCoordination && envelope.Purpose == RemoteTransferPeerProtocol.GrainPurpose;

    internal static object? ExecutePeer(DatabaseEngine database, IServiceProvider services, TimeProvider clock,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken cancellationToken)
        => GrainRemoteTransferRead.Execute(database, principal, request,
            services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, cancellationToken);

    internal static Task<RemoteQueueTransferReceiptRead> TryReceiptAsync(IServiceProvider services,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken cancellationToken)
        => services.GetService<IRemoteDocumentReadRouter>() is IRemoteQueueTransferReceiptRouter transfers
            ? transfers.InspectTransferAsync(request.Envelope, principal,
                GrainNativePayload.Read<InspectQueueTransferReceiptRequest>(request.Payload), cancellationToken)
            : Task.FromResult(new RemoteQueueTransferReceiptRead(false, null));
}
