namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueOrderProtocol.Alias)]
internal sealed record QueueOrderReference(
    [property: global::Orleans.Id(QueueOrderProtocol.MessageIdField)] string MessageId,
    [property: global::Orleans.Id(QueueOrderProtocol.SequenceField)] long ActiveOrderSequence,
    [property: global::Orleans.Id(QueueOrderProtocol.GenerationField)] long DeliveryGeneration);
