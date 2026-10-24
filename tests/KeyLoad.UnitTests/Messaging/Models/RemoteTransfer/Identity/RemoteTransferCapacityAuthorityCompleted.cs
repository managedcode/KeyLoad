using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record RemoteTransferCapacityAuthorityCompleted(RemoteTransferCapacityAuthorityTransfer Transfer,
    CommandRequest Accept, Result<object> Accepted, QueueTransferReceiptInspection Proof,
    CommandRequest Complete, Result<object> Completed, ReceiveRequest Receive, Result<object> Received,
    DeliveryCommand Acknowledge, Result<object> Acknowledged, long ReadySequence);
