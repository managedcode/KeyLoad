namespace KeyLoad;

/// <summary>Names the immutable Orleans wire and persisted-contract aliases for queue transfers.</summary>
internal static class QueueTransferContractAliases
{
    internal const string Create = "keyload.contract.create-queue-transfer";
    internal const string Accept = "keyload.contract.accept-queue-transfer";
    internal const string Complete = "keyload.contract.complete-queue-transfer";
    internal const string InspectRequest = "keyload.contract.inspect-queue-transfer-request";
    internal const string Inspection = "keyload.contract.queue-transfer-inspection";
    internal const string ReceiptInspectionRequest = "keyload.contract.inspect-queue-transfer-receipt-request";
    internal const string ReceiptInspection = "keyload.contract.queue-transfer-receipt-inspection";
    internal const string State = "keyload.contract.queue-transfer-state";
}
