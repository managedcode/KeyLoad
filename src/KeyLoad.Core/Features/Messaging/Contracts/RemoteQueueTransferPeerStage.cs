namespace KeyLoad.Core.Features.Messaging;

internal enum RemoteQueueTransferPeerStage
{
    Accept = RemoteTransferPeerProtocol.AcceptStage,
    Receipt = RemoteTransferPeerProtocol.ReceiptStage,
    Outcome = RemoteTransferPeerProtocol.OutcomeStage
}
