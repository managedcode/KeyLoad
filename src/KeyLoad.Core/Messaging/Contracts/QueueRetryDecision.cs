namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueRetryProtocol.DecisionAlias)]
internal sealed record QueueRetryDecision(
    [property: global::Orleans.Id(QueueRetryProtocol.Field0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(QueueRetryProtocol.Field1)] string MessageId,
    [property: global::Orleans.Id(QueueRetryProtocol.Field2)] long ExpectedStateVersion,
    [property: global::Orleans.Id(QueueRetryProtocol.Field3)] long ExpectedLeaseVersion,
    [property: global::Orleans.Id(QueueRetryProtocol.Field4)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueRetryProtocol.Field5)] int Attempts,
    [property: global::Orleans.Id(QueueRetryProtocol.Field6)] ReadOnlyMemory<byte> DueIndexKey,
    [property: global::Orleans.Id(QueueRetryProtocol.Field7)] ReadOnlyMemory<byte> PolicyDigest,
    [property: global::Orleans.Id(QueueRetryProtocol.Field8)] int DelayMilliseconds,
    [property: global::Orleans.Id(QueueRetryProtocol.Field9)] DateTimeOffset RetryAt);
