using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueOrderedRetryRecoveryChoice
{
    internal static async Task AssertAsync(ZoneTreeStore store, Command<DatabaseCommand> operation,
        QueueRetryDecisions actual)
    {
        var lane = new QueueLaneRef(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue);
        var policy = new QueuePolicy { OrderingProfile = QueueOrderingProfile.StrictPerKey, RetryJitter = QueueRetryJitter.Full };
        var seed = OrleansSerialization.Serialize(new QueueRetrySeed(operation.CommandId, lane, QueueOrderedRetryCrashProtocol.First,
            QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.Two));
        var digest = HMACSHA256.HashData(store.Identity.SigningKey.Span, seed);
        var delay = QueueOrderedRetryCrashProtocol.One + (int)(BinaryPrimitives.ReadUInt64BigEndian(digest) % (ulong)policy.RetryBaseMilliseconds);
        var expected = new QueueRetryDecisions(QueueOrderedRetryCrashProtocol.One, new DateTimeOffset(operation.Timestamp),
            ImmutableArray.Create(new QueueRetryDecision(lane, QueueOrderedRetryCrashProtocol.First, QueueOrderedRetryCrashProtocol.Two,
                QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.One,
                ReadOnlyMemory<byte>.Empty, SHA256.HashData(OrleansSerialization.Serialize(policy)), delay, new DateTimeOffset(operation.Timestamp).AddMilliseconds(delay))));
        await Assert.That(OrleansSerialization.Serialize(actual)).IsEquivalentTo(OrleansSerialization.Serialize(expected), CollectionOrdering.Matching);

    }
}
