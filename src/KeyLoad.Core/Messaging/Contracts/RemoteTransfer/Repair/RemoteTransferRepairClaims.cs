namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferRepairClaimsFields.Alias)]
internal sealed record RemoteTransferRepairClaims(
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.MessageFingerprint)] string MessageFingerprint,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.IntentDigest)] string IntentDigest,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.Stage)] QueueTransferRepairStage Stage,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.CapacityGeneration)] long CapacityGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.PolicyGeneration)] long PolicyGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.CompleteGeneration)] long CompleteGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.FailedCommandId)] Guid FailedCommandId,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.CommandFingerprint)] string CommandFingerprint,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.OutcomeDigest)] string OutcomeDigest,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.OriginalPolicyEpoch)] long OriginalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.CurrentPolicyEpoch)] long CurrentPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.CurrentFieldHeaderDigest)] string CurrentFieldHeaderDigest,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.OwnerCut)] CommitToken OwnerCut,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.ReadGeneration)] long ReadGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairClaimsFields.ReceiptDigest)] string? ReceiptDigest);
