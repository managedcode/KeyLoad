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

/// <summary>CAS one bounded source attempt after an authenticated actual target capacity repair.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueTransferAttemptProtocol.Alias)]
public sealed record AdvanceQueueTransferAttempt(
    [property: global::Orleans.Id(QueueTransferAttemptProtocol.SourceField)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(QueueTransferAttemptProtocol.TransferField)] Guid TransferId,
    [property: global::Orleans.Id(QueueTransferAttemptProtocol.GenerationField)] long ExpectedGeneration,
    [property: global::Orleans.Id(QueueTransferAttemptProtocol.WitnessField)] string FailureWitness) : Mutation(SourceQueue.Queue);
