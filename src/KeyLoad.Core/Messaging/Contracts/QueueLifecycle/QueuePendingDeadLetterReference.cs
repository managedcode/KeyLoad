namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueLifecycleProtocol.PendingAlias)]
internal sealed record QueuePendingDeadLetterReference(
    [property: global::Orleans.Id(QueueLifecycleProtocol.MessageId)] string MessageId,
    [property: global::Orleans.Id(QueueLifecycleProtocol.PendingGeneration)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueLifecycleProtocol.PendingReason)] string SafeFailureCode);
