namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueRetryProtocol.SeedAlias)]
internal sealed record QueueRetrySeed(
    [property: global::Orleans.Id(QueueRetryProtocol.Field0)] Guid OperationId,
    [property: global::Orleans.Id(QueueRetryProtocol.Field1)] QueueLaneRef Lane,
    [property: global::Orleans.Id(QueueRetryProtocol.Field2)] string MessageId,
    [property: global::Orleans.Id(QueueRetryProtocol.Field3)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueRetryProtocol.Field4)] int Attempts,
    [property: global::Orleans.Id(QueueRetryProtocol.Field5)] long StateVersion);
