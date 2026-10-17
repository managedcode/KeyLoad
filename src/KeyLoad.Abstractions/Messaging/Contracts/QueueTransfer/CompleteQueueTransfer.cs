namespace KeyLoad;

/// <summary>Completes a pending source intent using a signed destination receipt.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
/// <param name="ReceiptToken">The signed committed destination receipt.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Complete)]
public sealed record CompleteQueueTransfer(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId,
    [property: global::Orleans.Id(2)] string ReceiptToken) : Mutation(SourceQueue.Queue);
