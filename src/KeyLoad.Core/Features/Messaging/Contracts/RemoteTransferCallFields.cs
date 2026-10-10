namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCallFields
{
    internal const uint Version = 0;
    internal const uint RequestId = 1;
    internal const uint Nonce = 2;
    internal const uint ExpiresAt = 3;
    internal const uint CallerVoter = 4;
    internal const uint CallerSiloAddress = 5;
    internal const uint SourceOwner = 6;
    internal const uint DestinationOwner = 7;
    internal const uint Stage = 8;
    internal const uint OriginalCommandId = 9;
    internal const uint LogicalPrincipalId = 10;
    internal const uint LogicalPolicyEpoch = 11;
    internal const uint FieldHeaderDigest = 12;
    internal const uint IntentToken = 13;
    internal const uint IntentClaims = 14;
    internal const uint MaximumReplyBytes = 15;
    internal const uint SourceCut = 16;
}
