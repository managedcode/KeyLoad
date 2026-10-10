using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueOrderedRetryRecoveryChoice
{
    internal static async Task AssertAsync(ZoneTreeStore store, ReplicatedOperation operation,
        NativeCommandPayload payload, QueueRetryDecisions actual)
    {
        var lane = new QueueLaneRef(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue);
        var policy = new QueuePolicy { OrderingProfile = QueueOrderingProfile.StrictPerKey, RetryJitter = QueueRetryJitter.Full };
        var seed = NativeSerialization.Serialize(new QueueRetrySeed(operation.Id, lane, QueueOrderedRetryCrashProtocol.First,
            QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.Two));
        var digest = HMACSHA256.HashData(store.Identity.SigningKey.Span, seed);
        var delay = QueueOrderedRetryCrashProtocol.One + (int)(BinaryPrimitives.ReadUInt64BigEndian(digest) % (ulong)policy.RetryBaseMilliseconds);
        var expected = new QueueRetryDecisions(QueueOrderedRetryCrashProtocol.One, operation.EvaluatedAt,
            ImmutableArray.Create(new QueueRetryDecision(lane, QueueOrderedRetryCrashProtocol.First, QueueOrderedRetryCrashProtocol.Two,
                QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.One,
                ReadOnlyMemory<byte>.Empty, SHA256.HashData(NativeSerialization.Serialize(policy)), delay, operation.EvaluatedAt.AddMilliseconds(delay))));
        await Assert.That(NativeSerialization.Serialize(actual)).IsEquivalentTo(NativeSerialization.Serialize(expected), CollectionOrdering.Matching);
        var authority = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span);
        await Assert.That(authority.RetryDecisionHash.ToArray()).IsEquivalentTo(SHA256.HashData(payload.RetryDecisions.Span), CollectionOrdering.Matching);
    }
}
