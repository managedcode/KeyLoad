namespace KeyLoad.Core.Features.Messaging;

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
