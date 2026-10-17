
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferRepairArithmeticOriginal(CreateQueueTransfer Transfer,
    CommandRequest Create, CommitReceipt Created, byte[] CreateBytes,
    CommandRequest Denied, OperationResult Denial, byte[] DenialBytes,
    CommandRequest Accepted, CommitReceipt Acceptance, QueueTransferInspection Intent,
    QueueTransferReceiptInspection Proof, CommandRequest Complete, byte[] IntentKey, byte[] IntentBytes, string[] OriginalImage);
