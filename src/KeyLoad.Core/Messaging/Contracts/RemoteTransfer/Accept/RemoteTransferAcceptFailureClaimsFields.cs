namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAcceptFailureClaimsFields
{
    internal const uint Purpose = 0;
    internal const uint OriginalAuthority = 1;
    internal const uint OutcomeDigest = 2;
    internal const uint CurrentDependency = 3;
    internal const uint TargetCut = 4;
    internal const uint ReadGeneration = 5;
    internal const uint FailureKind = 6;
    internal const string Alias = "keyload.core.queue-transfer.accept-failure-claims.v1";
}
