namespace KeyLoad;

/// <summary>Conditionally moves a pending dead-letter message into its bounded retained namespace.</summary>
/// <param name="Queue">The queue owning the pending message.</param>
/// <param name="MessageId">The exact pending message identity.</param>
/// <param name="ExpectedStateVersion">The current state version required for parking.</param>
/// <param name="ExpectedDeliveryGeneration">The current delivery generation required for parking.</param>
[Orleans.GenerateSerializer, Orleans.Alias(QueueLifecycleContractProtocol.ParkAlias)]
public sealed record ParkPendingQueueMessage(
    [property: Orleans.Id(QueueLifecycleContractProtocol.QueueField)] string Queue,
    [property: Orleans.Id(QueueLifecycleContractProtocol.MessageField)] string MessageId,
    [property: Orleans.Id(QueueLifecycleContractProtocol.VersionField)] long ExpectedStateVersion,
    [property: Orleans.Id(QueueLifecycleContractProtocol.GenerationField)] long ExpectedDeliveryGeneration) : Mutation(Queue);
