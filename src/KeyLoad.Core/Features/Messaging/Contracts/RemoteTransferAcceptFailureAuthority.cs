namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAcceptFailureAuthorityFields
{
    internal const uint Source = 0;
    internal const uint Destination = 1;
    internal const uint TransferId = 2;
    internal const uint PrincipalId = 3;
    internal const uint MessageFingerprint = 4;
    internal const uint IntentDigest = 5;
    internal const uint AcceptCommandId = 6;
    internal const uint CommandFingerprint = 7;
    internal const uint TargetIncarnation = 8;
    internal const uint Dependency = 9;
    internal const string Alias = "keyload.core.queue-transfer.accept-failure-authority.v1";
}

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
