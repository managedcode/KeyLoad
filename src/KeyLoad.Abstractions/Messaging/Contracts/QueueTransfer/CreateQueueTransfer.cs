namespace KeyLoad;

/// <summary>Persists an immutable source intent for one stable transfer identity.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
/// <param name="Destination">The complete destination queue lane.</param>
/// <param name="Message">The exact message to enqueue at the destination.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Create)]
public sealed record CreateQueueTransfer(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId,
    [property: global::Orleans.Id(2)] QueueLaneRef Destination,
    [property: global::Orleans.Id(3)] EnqueueMessage Message) : Mutation(SourceQueue.Queue);
