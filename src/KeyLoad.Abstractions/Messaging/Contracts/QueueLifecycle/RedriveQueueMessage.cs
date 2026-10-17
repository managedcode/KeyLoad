namespace KeyLoad;

/// <summary>Conditionally redrives a parked message as a new delivery generation.</summary>
/// <param name="Queue">The queue owning the retained message.</param>
/// <param name="MessageId">The exact retained message identity.</param>
/// <param name="ExpectedStateVersion">The current state version required for the transition.</param>
/// <param name="ExpectedDeliveryGeneration">The current delivery generation required for the transition.</param>
/// <param name="NotBefore">The optional earliest delivery time for the new generation.</param>
[Orleans.GenerateSerializer, Orleans.Alias(QueueLifecycleContractProtocol.RedriveAlias)]
public sealed record RedriveQueueMessage(
    [property: Orleans.Id(QueueLifecycleContractProtocol.QueueField)] string Queue,
    [property: Orleans.Id(QueueLifecycleContractProtocol.MessageField)] string MessageId,
    [property: Orleans.Id(QueueLifecycleContractProtocol.VersionField)] long ExpectedStateVersion,
    [property: Orleans.Id(QueueLifecycleContractProtocol.GenerationField)] long ExpectedDeliveryGeneration,
    [property: Orleans.Id(QueueLifecycleContractProtocol.NotBeforeField)] DateTimeOffset? NotBefore = null) : Mutation(Queue);
