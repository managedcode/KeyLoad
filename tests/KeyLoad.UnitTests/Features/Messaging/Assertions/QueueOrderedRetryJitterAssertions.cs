using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryJitterAssertions
{
    internal static async Task<DateTimeOffset> RecordedAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var operation = state.Outcomes.Last().Operation;
        var wrapper = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var actual = NativeSerialization.Deserialize<QueueRetryDecisions>(wrapper.RetryDecisions.Span);
        var policy = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue)))!.QueuePolicy;
        var seed = NativeSerialization.Serialize(new QueueRetrySeed(operation.Id, state.Lane, QueueOrderedRetryProtocol.First,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Two));
        var digest = HMACSHA256.HashData(database.Store.Identity.SigningKey.Span, seed);
        var delay = QueueOrderedRetryProtocol.One + (int)(BinaryPrimitives.ReadUInt64BigEndian(digest) % (ulong)policy.RetryBaseMilliseconds);
        var at = operation.EvaluatedAt.AddMilliseconds(delay);
        var expected = new QueueRetryDecisions(QueueOrderedRetryProtocol.One, operation.EvaluatedAt,
            ImmutableArray.Create(new QueueRetryDecision(state.Lane, QueueOrderedRetryProtocol.First, QueueOrderedRetryProtocol.Two,
                QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.One, ReadOnlyMemory<byte>.Empty,
                SHA256.HashData(NativeSerialization.Serialize(policy)), delay, at)));
        await Assert.That(NativeSerialization.Serialize(actual)).IsEquivalentTo(NativeSerialization.Serialize(expected), CollectionOrdering.Matching);
        var authority = NativeSerialization.Deserialize<NativeCommandAuthority>(wrapper.Authority.Span);
        await Assert.That(authority.RetryDecisionHash.ToArray()).IsEquivalentTo(SHA256.HashData(wrapper.RetryDecisions.Span), CollectionOrdering.Matching);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Scheduled,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.One, at, null,
            LeaseVersion: QueueOrderedRetryProtocol.One, SafeFailureCode: "RetryRequested",
            EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: QueueOrderedRetryProtocol.One), stored: true);
        await ForgedAsync(database, state, operation, wrapper, actual);
        return at;
    }

    private static async Task ForgedAsync(DatabaseEngine database, QueueOrderedRetryState state,
        ReplicatedOperation operation, NativeCommandPayload wrapper, QueueRetryDecisions original)
    {
        var modified = original with
        {
            Items = original.Items.SetItem(QueueOrderedRetryProtocol.Initial,
            original.Items[QueueOrderedRetryProtocol.Initial] with { DelayMilliseconds = original.Items[QueueOrderedRetryProtocol.Initial].DelayMilliseconds + QueueOrderedRetryProtocol.One })
        };
        var forged = operation with
        {
            NativePayload = NativeSerialization.Serialize(wrapper with
            { RetryDecisions = NativeSerialization.Serialize(modified) })
        };
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(forged));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}
