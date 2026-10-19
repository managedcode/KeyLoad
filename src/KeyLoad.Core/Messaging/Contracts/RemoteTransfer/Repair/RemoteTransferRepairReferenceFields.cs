namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairReferenceFields
{
    internal const uint Stage = 0;
    internal const uint CapacityGeneration = 1;
    internal const uint PolicyGeneration = 2;
    internal const uint CompleteGeneration = 3;
    internal const uint FailedCommandId = 4;
    internal const uint Fingerprint = 5;
    internal const uint OutcomeDigest = 6;
    internal const uint WitnessToken = 7;
    internal const string Alias = "keyload.core.queue-transfer.repair-reference";
}
