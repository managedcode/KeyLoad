using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Messaging;

internal static class QueueRetryProtocol
{
    internal const string EnvelopeAlias = "keyload.core.queue-retry-decisions.v1";
    internal const string DecisionAlias = "keyload.core.queue-retry-decision.v1";
    internal const string SeedAlias = "keyload.core.queue-retry-seed.v1";
    internal const int Initial = 0;
    internal const int Positive = 1;
    internal const int Version = 1;
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
    internal const int Field3 = 3;
    internal const int Field4 = 4;
    internal const int Field5 = 5;
    internal const int Field6 = 6;
    internal const int Field7 = 7;
    internal const int Field8 = 8;
    internal const int Field9 = 9;
    internal const int DigestBytes = 32;
    internal const string InvalidDecision = "The recorded queue retry decision is invalid.";
    internal const string MissingDecision = "The recorded queue retry decision is unavailable.";
    internal const string StaleDecision = "The recorded queue retry decision no longer matches its input.";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueRetryProtocol.EnvelopeAlias)]
internal sealed record QueueRetryDecisions(
    [property: global::Orleans.Id(QueueRetryProtocol.Field0)] int Version,
    [property: global::Orleans.Id(QueueRetryProtocol.Field1)] DateTimeOffset EvaluatedAt,
    [property: global::Orleans.Id(QueueRetryProtocol.Field2)] ImmutableArray<QueueRetryDecision> Items);

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

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueRetryProtocol.SeedAlias)]
internal sealed record QueueRetrySeed(
    [property: global::Orleans.Id(QueueRetryProtocol.Field0)] Guid OperationId,
    [property: global::Orleans.Id(QueueRetryProtocol.Field1)] QueueLaneRef Lane,
    [property: global::Orleans.Id(QueueRetryProtocol.Field2)] string MessageId,
    [property: global::Orleans.Id(QueueRetryProtocol.Field3)] long DeliveryGeneration,
    [property: global::Orleans.Id(QueueRetryProtocol.Field4)] int Attempts,
    [property: global::Orleans.Id(QueueRetryProtocol.Field5)] long StateVersion);
