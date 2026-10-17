namespace KeyLoad;

internal static class QueueLifecycleContractProtocol
{
    internal const int QueueField = 0;
    internal const int MessageField = 1;
    internal const int VersionField = 2;
    internal const int GenerationField = 3;
    internal const int NotBeforeField = 4;
    internal const string RedriveKind = "redriveQueueMessage";
    internal const string CancelKind = "cancelQueueMessage";
    internal const string ParkKind = "parkPendingQueueMessage";
    internal const string RedriveAlias = "keyload.contract.redrive-queue-message.v1";
    internal const string CancelAlias = "keyload.contract.cancel-queue-message.v1";
    internal const string ParkAlias = "keyload.contract.park-pending-queue-message.v1";
}

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
