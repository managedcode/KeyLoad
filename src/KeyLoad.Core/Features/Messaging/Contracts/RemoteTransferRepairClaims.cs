namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairClaimsFields
{
    internal const uint Purpose = 0;
    internal const uint Source = 1;
    internal const uint Destination = 2;
    internal const uint TransferId = 3;
    internal const uint PrincipalId = 4;
    internal const uint MessageFingerprint = 5;
    internal const uint IntentDigest = 6;
    internal const uint Stage = 7;
    internal const uint CapacityGeneration = 8;
    internal const uint PolicyGeneration = 9;
    internal const uint CompleteGeneration = 10;
    internal const uint FailedCommandId = 11;
    internal const uint CommandFingerprint = 12;
    internal const uint OutcomeDigest = 13;
    internal const uint OriginalPolicyEpoch = 14;
    internal const uint CurrentPolicyEpoch = 15;
    internal const uint CurrentFieldHeaderDigest = 16;
    internal const uint OwnerCut = 17;
    internal const uint ReadGeneration = 18;
    internal const uint ReceiptDigest = 19;
    internal const string Alias = "keyload.core.queue-transfer.repair-claims.v1";
}

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
