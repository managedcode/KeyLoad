namespace KeyLoad;

internal static class QueueTransferAttemptProtocol
{
    internal const string Alias = "keyload.queue-transfer.advance-attempt.v1";
    internal const string Kind = "advanceQueueTransferAttempt";
    internal const uint SourceField = 0;
    internal const uint TransferField = 1;
    internal const uint GenerationField = 2;
    internal const uint WitnessField = 3;
}
