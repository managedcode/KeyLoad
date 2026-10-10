namespace KeyLoad.Core.Features.Messaging;

internal static class QueueLifecycleProtocol
{
    internal const string PendingSpace = "queue-pending-dead-letter";
    internal const string ParkedSpace = "queue-dead-letter-order";
    internal const string RedriveKind = "redriveQueueMessage";
    internal const string CancelKind = "cancelQueueMessage";
    internal const string ParkKind = "parkPendingQueueMessage";
    internal const string PendingAlias = "keyload.core.queue-pending-dead-letter-reference.v1";
    internal const string ParkedAlias = "keyload.core.queue-dead-letter-reference.v1";
    internal const int Initial = 0;
    internal const int Increment = 1;
    internal const string MissingBody = "The queue lifecycle body is unavailable.";
    internal const string MissingMetadata = "The queue lifecycle metadata is unavailable.";
    internal const string InvalidCounter = "The queue lifecycle accounting is invalid.";
    internal const string MissingReference = "The queue lifecycle reference does not match its metadata.";
    internal const string StaleCondition = "The queue lifecycle condition is stale.";
    internal const string InvalidState = "The queue lifecycle transition is not permitted from this state.";
    internal const string InvalidShape = "The queue lifecycle identity or condition is invalid.";
    internal const string AdministratorRequired = "Queue lifecycle administration is required.";
    internal const string FullDeadLetter = "The queue dead-letter sublimit is exhausted.";
    internal const string ExpiredRedrive = "The original queue message expiry prevents this transition.";
    internal const int MessageId = 0;
    internal const int PendingGeneration = 1;
    internal const int PendingReason = 2;
    internal const int ParkedSequence = 1;
    internal const int ParkedGeneration = 2;
    internal const int ParkedReason = 3;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueLifecycleProtocol.PendingAlias)]
internal sealed record QueuePendingDeadLetterReference(
    [property: global::Orleans.Id(QueueLifecycleProtocol.MessageId)] string MessageId,
    [property: global::Orleans.Id(QueueLifecycleProtocol.PendingGeneration)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueLifecycleProtocol.PendingReason)] string SafeFailureCode);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueLifecycleProtocol.ParkedAlias)]
internal sealed record QueueDeadLetterReference(
    [property: global::Orleans.Id(QueueLifecycleProtocol.MessageId)] string MessageId,
    [property: global::Orleans.Id(QueueLifecycleProtocol.ParkedSequence)] long Sequence,
    [property: global::Orleans.Id(QueueLifecycleProtocol.ParkedGeneration)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueLifecycleProtocol.ParkedReason)] string SafeFailureCode);
