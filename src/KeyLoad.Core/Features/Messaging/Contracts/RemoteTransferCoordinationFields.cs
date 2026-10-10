namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationFields
{
    internal const uint Source = 0;
    internal const uint Destination = 1;
    internal const uint TransferId = 2;
    internal const uint PrincipalId = 3;
    internal const uint Fingerprint = 4;
    internal const uint IntentDigest = 5;
    internal const uint SourceCut = 6;
    internal const uint AcceptGeneration = 7;
    internal const uint AcceptAttemptCeiling = 8;
    internal const uint AcceptPolicyGeneration = 9;
    internal const uint CompleteGeneration = 10;
    internal const uint RepairCeiling = 11;
}
