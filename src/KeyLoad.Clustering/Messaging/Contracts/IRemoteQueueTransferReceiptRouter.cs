using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal interface IRemoteQueueTransferReceiptRouter
{
    Task<RemoteQueueTransferReceiptRead> InspectTransferAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken);
}

internal sealed record RemoteQueueTransferReceiptRead(bool Routed, QueueTransferReceiptInspection? Receipt);
