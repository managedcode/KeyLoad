namespace KeyLoad;

internal static class QueueTransferRepairProtocol
{
    internal const string Alias = "keyload.queue-transfer.advance-repair.v1";
    internal const string Kind = "advanceQueueTransferRepair";
    internal const uint SourceQueue = 0;
    internal const uint TransferId = 1;
    internal const uint Stage = 2;
    internal const uint ExpectedCapacityGeneration = 3;
    internal const uint ExpectedPolicyGeneration = 4;
    internal const uint ExpectedCompleteGeneration = 5;
    internal const uint FailureWitness = 6;
}
