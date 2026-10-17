namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.ExternalReceiptAlias)]
internal sealed record RemoteTransferExternalReceipt(
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.SourceOwner)] RegisteredPhysicalOwnerV1 SourceOwner,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.DestinationOwner)] RegisteredPhysicalOwnerV1 DestinationOwner,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.LogicalPrincipalId)] string LogicalPrincipalId,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.OriginalTargetReceiptToken)] string OriginalTargetReceiptToken,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.TargetCommit)] CommitToken TargetCommit,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.SourceCut)] CommitToken SourceCut,
    [property: global::Orleans.Id(RemoteTransferExternalReceiptFields.OriginalTargetReceiptDigest)] string OriginalTargetReceiptDigest);
