using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;
namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class TopicPurgeReceiptAssertions
{
    private const string PinnedDetail = "The topic purge cut is pinned by a subscription checkpoint.";
    internal static async Task VerifyAsync(IAtomicStore store, ReplicatedOperation operation, OperationResult outcome, long position, bool pinned)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(outcome.Error).IsEqualTo(pinned ? ErrorCode.ResourceExhausted : (ErrorCode?)null);
        if (pinned)
        { await Assert.That(outcome.SafeDetail).IsEqualTo(PinnedDetail); await Assert.That(outcome.NativeValue).IsNull(); }
        else
        {
            var receipt = outcome.Get<CommitReceipt>();
            await Assert.That(receipt.CommandId).IsEqualTo(TopicPurgeCrashContract.PurgeId);
            await Assert.That(receipt.Token.Position).IsEqualTo(position);
            await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(TopicPurgeCrashContract.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(1L);
            await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
            await Assert.That(receipt.Mutations).IsEquivalentTo(new[] { new MutationReceipt("purgeTopic", TopicPurgeCrashContract.Topic, "2", 2) }, CollectionOrdering.Matching);
        }
        var stored = OutcomeStoreOracle.ReadStored(store, operation)!;
        await Assert.That(stored.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(stored.Partition).IsEqualTo(TopicPurgeCrashContract.Partition);
        var canonical = "{\"id\":" + JsonSerializer.Serialize(operation.Id, JsonDefaults.Options) + ",\"kind\":" +
            JsonSerializer.Serialize(OperationKind.Batch, JsonDefaults.Options) + ",\"payloadJson\":" +
            JsonSerializer.Serialize(operation.PayloadJson, JsonDefaults.Options) + ",\"principalId\":\"root\"}";
        await Assert.That(stored.Fingerprint).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        await EventAppendReceiptAssertions.SameResultAsync(outcome, stored.Result);
        await Assert.That(store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.Clock.ToArray())!))).IsEqualTo(operation.EvaluatedAt);
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.Applied.ToArray()))).IsNull();
    }
    internal static async Task StableReplayAsync(DatabaseEngine database, ReplicatedOperation operation, OperationResult expected)
    {
        var position = database.Store.Position;
        var bytes = EventAppendStateAssertions.Bytes(database.Store);
        await EventAppendReceiptAssertions.SameResultAsync(expected, database.Apply(operation));
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await EventAppendStateAssertions.SameBytesAsync(bytes, database.Store);
    }
}
