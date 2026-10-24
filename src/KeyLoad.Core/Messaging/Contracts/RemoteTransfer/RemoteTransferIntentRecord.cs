namespace KeyLoad.Core.Messaging;

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
    [property: global::Orleans.Id(RemoteTransferFields.ReceiptReservationBytes)] int ReceiptReservationBytes)
{
    [global::Orleans.Id(RemoteTransferFields.AcceptAttempts)]
    public RemoteTransferAcceptAttemptState? Attempts
    {
        get; init;
    }

    [global::Orleans.Id(RemoteTransferFields.Repairs)]
    public RemoteTransferRepairState? Repairs
    {
        get; init;
    }

}
