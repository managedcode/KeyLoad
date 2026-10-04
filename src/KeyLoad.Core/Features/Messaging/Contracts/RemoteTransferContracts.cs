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

internal static class RemoteTransferAliases
{
    internal const string IntentClaims = "keyload.core.remote-transfer-intent-claims.v1";
    internal const string ReceiptClaims = "keyload.core.remote-transfer-receipt-claims.v1";
    internal const string IntentRecord = "keyload.core.remote-transfer-intent-record.v1";
    internal const string TargetReceiptRecord = "keyload.core.remote-transfer-target-receipt.v1";
    internal const string Capacity = "keyload.core.remote-transfer-capacity.v1";
}

internal static class RemoteTransferFields
{
    internal const int Purpose = 0;
    internal const int Incarnation = 1;
    internal const int Source = 2;
    internal const int TransferId = 3;
    internal const int Destination = 4;
    internal const int PrincipalId = 5;
    internal const int Message = 6;
    internal const int Fingerprint = 7;
    internal const int TargetCommit = 8;
    internal const int State = 9;
    internal const int IntentToken = 10;
    internal const int ReceiptToken = 11;
    internal const int ReceiptReservationBytes = 12;
    internal const int StoredBytes = 13;
    internal const int StoredRecords = 14;
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RemoteTransferAliases.IntentClaims)]
internal sealed record RemoteTransferIntentClaims(
    [property: global::Orleans.Id(RemoteTransferFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(RemoteTransferFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(RemoteTransferFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferFields.Message)] EnqueueMessage Message,
    [property: global::Orleans.Id(RemoteTransferFields.Fingerprint)] string Fingerprint);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RemoteTransferAliases.ReceiptClaims)]
internal sealed record RemoteTransferReceiptClaims(
    [property: global::Orleans.Id(RemoteTransferFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(RemoteTransferFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(RemoteTransferFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(RemoteTransferFields.TargetCommit)] CommitToken TargetCommit);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RemoteTransferAliases.IntentRecord)]
internal sealed record RemoteTransferIntentRecord(
    [property: global::Orleans.Id(RemoteTransferFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferFields.Message)] EnqueueMessage Message,
    [property: global::Orleans.Id(RemoteTransferFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(RemoteTransferFields.State)] QueueTransferState State,
    [property: global::Orleans.Id(RemoteTransferFields.IntentToken)] string IntentToken,
    [property: global::Orleans.Id(RemoteTransferFields.ReceiptToken)] string? ReceiptToken,
    [property: global::Orleans.Id(RemoteTransferFields.ReceiptReservationBytes)] int ReceiptReservationBytes);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RemoteTransferAliases.TargetReceiptRecord)]
internal sealed record RemoteTransferTargetReceiptRecord(
    [property: global::Orleans.Id(RemoteTransferFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(RemoteTransferFields.ReceiptToken)] string ReceiptToken,
    [property: global::Orleans.Id(RemoteTransferFields.TargetCommit)] CommitToken TargetCommit);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RemoteTransferAliases.Capacity)]
internal sealed record RemoteTransferCapacity(
    [property: global::Orleans.Id(RemoteTransferFields.StoredRecords)] long StoredRecords,
    [property: global::Orleans.Id(RemoteTransferFields.StoredBytes)] long StoredBytes);
