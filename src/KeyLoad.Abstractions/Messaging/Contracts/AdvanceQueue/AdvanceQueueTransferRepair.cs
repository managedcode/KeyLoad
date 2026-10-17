namespace KeyLoad;

/// <summary>CAS bounded source history after fresh authorization of an exact known denied transfer.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueTransferRepairProtocol.Alias)]
public sealed record AdvanceQueueTransferRepair(
    [property: global::Orleans.Id(QueueTransferRepairProtocol.SourceQueue)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(QueueTransferRepairProtocol.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(QueueTransferRepairProtocol.Stage)] QueueTransferRepairStage Stage,
    [property: global::Orleans.Id(QueueTransferRepairProtocol.ExpectedCapacityGeneration)] long ExpectedCapacityGeneration,
    [property: global::Orleans.Id(QueueTransferRepairProtocol.ExpectedPolicyGeneration)] long ExpectedPolicyGeneration,
    [property: global::Orleans.Id(QueueTransferRepairProtocol.ExpectedCompleteGeneration)] long ExpectedCompleteGeneration,
    [property: global::Orleans.Id(QueueTransferRepairProtocol.FailureWitness)] string FailureWitness) : Mutation(SourceQueue.Queue);
