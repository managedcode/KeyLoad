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
    internal const string Alias = "keyload.core.queue-transfer.accept-failure-authority";
}
