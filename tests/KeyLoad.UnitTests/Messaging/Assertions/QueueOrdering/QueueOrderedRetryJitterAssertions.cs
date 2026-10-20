using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryJitterAssertions
{
    internal static async Task<DateTimeOffset> RecordedAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var operation = state.Outcomes.Last().Operation;
        var wrapper = operation.Value!;
        var actual = OrleansSerialization.Deserialize<QueueRetryDecisions>(wrapper.RetryDecisions.Span);
        var policy = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue)))!.QueuePolicy;
        var seed = OrleansSerialization.Serialize(new QueueRetrySeed(operation.CommandId, state.Lane, QueueOrderedRetryProtocol.First,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Two));
        var digest = HMACSHA256.HashData(database.Store.Identity.SigningKey.Span, seed);
        var delay = QueueOrderedRetryProtocol.One + (int)(BinaryPrimitives.ReadUInt64BigEndian(digest) % (ulong)policy.RetryBaseMilliseconds);
        var at = new DateTimeOffset(operation.Timestamp).AddMilliseconds(delay);
        var expected = new QueueRetryDecisions(QueueOrderedRetryProtocol.One, new DateTimeOffset(operation.Timestamp),
            ImmutableArray.Create(new QueueRetryDecision(state.Lane, QueueOrderedRetryProtocol.First, QueueOrderedRetryProtocol.Two,
                QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.One, ReadOnlyMemory<byte>.Empty,
                SHA256.HashData(OrleansSerialization.Serialize(policy)), delay, at)));
        await Assert.That(OrleansSerialization.Serialize(actual)).IsEquivalentTo(OrleansSerialization.Serialize(expected), CollectionOrdering.Matching);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Scheduled,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.One, at, null,
            LeaseVersion: QueueOrderedRetryProtocol.One, SafeFailureCode: "RetryRequested",
            EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: QueueOrderedRetryProtocol.One), stored: true);
        return at;
    }

}
