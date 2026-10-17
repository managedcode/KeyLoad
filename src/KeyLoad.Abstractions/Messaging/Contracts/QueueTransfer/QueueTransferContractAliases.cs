namespace KeyLoad;

/// <summary>Names the immutable Orleans wire and persisted-contract aliases for queue transfers.</summary>
internal static class QueueTransferContractAliases
{
    internal const string Create = "keyload.contract.create-queue-transfer.v1";
    internal const string Accept = "keyload.contract.accept-queue-transfer.v1";
    internal const string Complete = "keyload.contract.complete-queue-transfer.v1";
    internal const string InspectRequest = "keyload.contract.inspect-queue-transfer-request.v1";
    internal const string Inspection = "keyload.contract.queue-transfer-inspection.v1";
    internal const string ReceiptInspectionRequest = "keyload.contract.inspect-queue-transfer-receipt-request.v1";
    internal const string ReceiptInspection = "keyload.contract.queue-transfer-receipt-inspection.v1";
    internal const string State = "keyload.contract.queue-transfer-state.v1";
}
