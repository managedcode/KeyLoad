namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptFailureAuthorityFields.Alias)]
internal sealed record RemoteTransferAcceptFailureAuthority(
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.MessageFingerprint)] string MessageFingerprint,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.IntentDigest)] string IntentDigest,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.AcceptCommandId)] Guid AcceptCommandId,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.CommandFingerprint)] string CommandFingerprint,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.TargetIncarnation)] Guid TargetIncarnation,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureAuthorityFields.Dependency)] RemoteTransferAcceptDependency Dependency);
