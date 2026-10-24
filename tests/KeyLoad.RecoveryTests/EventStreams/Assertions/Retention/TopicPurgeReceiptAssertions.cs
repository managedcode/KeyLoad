using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;
namespace KeyLoad.RecoveryTests.EventStreams;

internal static class TopicPurgeReceiptAssertions
{
    private const string PinnedDetail = "The topic purge cut is pinned by a subscription checkpoint.";
    internal static async Task VerifyAsync(IAtomicStore store, Command<DatabaseCommand> operation, Result<object> outcome, long position, bool pinned)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(Errors.Code(outcome.Problem)).IsEqualTo(pinned ? ErrorCode.ResourceExhausted : (ErrorCode?)null);
        var stored = OutcomeStoreOracle.ReadStored(store, operation)!;
        if (pinned)
        {
            await Assert.That(outcome.Problem?.Detail).IsEqualTo(PinnedDetail);
            await Assert.That(outcome.Value).IsNull();
        }
        else
        {
            var receipt = outcome.Get<CommitReceipt>();
            await Assert.That(receipt.CommandId).IsEqualTo(TopicPurgeCrashContract.PurgeId);
            await Assert.That(receipt.Token.Position).IsEqualTo(stored.CanonicalSequence);
            await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(TopicPurgeCrashContract.Partition.AtomicPartitionId);

            await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
            await Assert.That(receipt.Mutations).IsEquivalentTo(new[] { new MutationReceipt("purgeTopic", TopicPurgeCrashContract.Topic, "2", 2) }, CollectionOrdering.Matching);
        }
        await Assert.That(stored.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(stored.Partition).IsEqualTo(TopicPurgeCrashContract.Partition);
        var canonical = "{\"id\":" + JsonSerializer.Serialize(operation.CommandId, JsonDefaults.Options) + ",\"kind\":" +
            JsonSerializer.Serialize(OperationKind.Batch, JsonDefaults.Options) + ",\"payloadJson\":" +
            JsonSerializer.Serialize(JsonSerializer.Serialize(OrleansSerialization.Deserialize<CommandRequest>(operation.Value!.Input.Span),
                JsonDefaults.Options), JsonDefaults.Options) + ",\"principalId\":\"root\"}";
        await Assert.That(stored.Fingerprint).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        await EventAppendReceiptAssertions.SameResultAsync(outcome, stored.Result);
        await Assert.That(store.Read(view => OrleansSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.Clock.ToArray())!))).IsEqualTo(new DateTimeOffset(operation.Timestamp));
        await Assert.That(stored.CanonicalSequence).IsGreaterThan(0L);
        await Assert.That(store.Read(view => OrleansSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw new InvalidOperationException("The canonical applied position is missing.")))).IsEqualTo(stored.CanonicalSequence);
    }
    internal static async Task StableReplayAsync(DatabaseEngine database, Command<DatabaseCommand> operation, Result<object> expected)
    {
        var position = database.Store.Position;
        var bytes = EventAppendStateAssertions.Bytes(database.Store);
        await EventAppendReceiptAssertions.SameResultAsync(expected, database.Apply(operation));
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await EventAppendStateAssertions.SameBytesAsync(bytes, database.Store);
    }
}
