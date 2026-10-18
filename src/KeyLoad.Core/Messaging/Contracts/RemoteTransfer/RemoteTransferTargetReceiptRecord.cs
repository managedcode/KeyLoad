namespace KeyLoad.Core.Features.Messaging;

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
