namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAttemptOriginalIdentity
{
    internal static bool Matches(RemoteTransferCoordinationHint hint, RemoteTransferAcceptFailureAuthority original)
        => original.Source == hint.Source && original.Destination == hint.Destination && original.TransferId == hint.TransferId
            && original.PrincipalId == hint.PrincipalId && original.MessageFingerprint == hint.Fingerprint
            && original.IntentDigest == hint.IntentDigest;
}
