namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueLifecycleProtocol.ParkedAlias)]
internal sealed record QueueDeadLetterReference(
    [property: global::Orleans.Id(QueueLifecycleProtocol.MessageId)] string MessageId,
    [property: global::Orleans.Id(QueueLifecycleProtocol.ParkedSequence)] long Sequence,
    [property: global::Orleans.Id(QueueLifecycleProtocol.ParkedGeneration)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueLifecycleProtocol.ParkedReason)] string SafeFailureCode);
