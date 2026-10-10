namespace KeyLoad.Orleans;

internal interface IRemoteQueueTransferReceiptRouter
{
    Task<RemoteQueueTransferReceiptRead> InspectTransferAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken);
}

internal sealed record RemoteQueueTransferReceiptRead(bool Routed, QueueTransferReceiptInspection? Receipt);
