using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal interface IRemoteQueueTransferReceiptRouter
{
    Task<RemoteQueueTransferReceiptRead> InspectTransferAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding);
}

internal sealed record RemoteQueueTransferReceiptRead(bool Routed, QueueTransferReceiptInspection? Receipt);
