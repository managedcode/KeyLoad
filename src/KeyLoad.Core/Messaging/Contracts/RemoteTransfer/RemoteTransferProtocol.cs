namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferProtocol
{
    internal const string IntentPurpose = "keyload.queue-transfer.intent.v1";
    internal const string ReceiptPurpose = "keyload.queue-transfer.receipt.v1";
    internal const string IntentSpace = "queue-transfer-intent";
    internal const string SourceCapacitySpace = "queue-transfer-source-capacity";
    internal const string TargetReceiptSpace = "queue-transfer-target-receipt";
    internal const string TargetCapacitySpace = "queue-transfer-target-capacity";
    internal const string CreateReceiptKind = "createQueueTransfer";
    internal const string AcceptReceiptKind = "acceptQueueTransfer";
    internal const string CompleteReceiptKind = "completeQueueTransfer";
    internal const string TransferIdFormat = "N";
}
