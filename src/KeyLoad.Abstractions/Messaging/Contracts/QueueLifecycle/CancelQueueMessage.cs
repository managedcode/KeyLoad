namespace KeyLoad;

/// <summary>Conditionally cancels a queued message and releases its charged storage.</summary>
/// <param name="Queue">The queue owning the retained message.</param>
/// <param name="MessageId">The exact retained message identity.</param>
/// <param name="ExpectedStateVersion">The current state version required for cancellation.</param>
/// <param name="ExpectedDeliveryGeneration">The current delivery generation required for cancellation.</param>
[Orleans.GenerateSerializer, Orleans.Alias(QueueLifecycleContractProtocol.CancelAlias)]
public sealed record CancelQueueMessage(
    [property: Orleans.Id(QueueLifecycleContractProtocol.QueueField)] string Queue,
    [property: Orleans.Id(QueueLifecycleContractProtocol.MessageField)] string MessageId,
    [property: Orleans.Id(QueueLifecycleContractProtocol.VersionField)] long ExpectedStateVersion,
    [property: Orleans.Id(QueueLifecycleContractProtocol.GenerationField)] long ExpectedDeliveryGeneration) : Mutation(Queue);
