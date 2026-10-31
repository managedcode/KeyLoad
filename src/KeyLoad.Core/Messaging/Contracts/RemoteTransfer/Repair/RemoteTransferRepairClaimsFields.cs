namespace KeyLoad.Core.Messaging;

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
    // Field 12 was the failed command fingerprint. It is retired and must never be reused.
    internal const uint OutcomeDigest = 13;
    internal const uint OriginalPolicyEpoch = 14;
    internal const uint CurrentPolicyEpoch = 15;
    internal const uint CurrentFieldHeaderDigest = 16;
    internal const uint OwnerCut = 17;
    internal const uint ReadGeneration = 18;
    internal const uint ReceiptDigest = 19;
    internal const string Alias = "keyload.core.queue-transfer.repair-claims";
}
