namespace KeyLoad;

/// <summary>Accepts a signed source intent on its destination partition.</summary>
/// <param name="DestinationQueue">The complete destination queue lane.</param>
/// <param name="IntentToken">The signed immutable source intent.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Accept)]
public sealed record AcceptQueueTransfer(
    [property: global::Orleans.Id(0)] QueueLaneRef DestinationQueue,
    [property: global::Orleans.Id(1)] string IntentToken) : Mutation(DestinationQueue.Queue);
