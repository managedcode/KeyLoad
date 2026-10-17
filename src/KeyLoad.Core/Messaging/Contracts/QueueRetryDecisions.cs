using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueRetryProtocol.EnvelopeAlias)]
internal sealed record QueueRetryDecisions(
    [property: global::Orleans.Id(QueueRetryProtocol.Field0)] int Version,
    [property: global::Orleans.Id(QueueRetryProtocol.Field1)] DateTimeOffset EvaluatedAt,
    [property: global::Orleans.Id(QueueRetryProtocol.Field2)] ImmutableArray<QueueRetryDecision> Items);
