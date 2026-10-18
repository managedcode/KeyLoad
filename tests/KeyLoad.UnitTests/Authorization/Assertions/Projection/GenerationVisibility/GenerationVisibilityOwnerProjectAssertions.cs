using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilityOwnerProjectAssertions
{
    private const string Sql = "SELECT * FROM \"visibility-owner-project\" ORDER BY id";
    private const string Scan = "bounded-full-scan";
    private const long InitialOwnershipEpoch = 1;

    internal static async Task ReadAsync(DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        PartitionRef partition, string[] visible, long ownRevision, long sharedRevision, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var expected = visible.Select(id => new QueryRow(id,
            id == GenerationVisibilityOwnerProjectFlow.Own ? ownRevision : sharedRevision,
            id == GenerationVisibilityOwnerProjectFlow.Own ? GenerationVisibilityOwnerProjectFlow.OwnJson
                : GenerationVisibilityOwnerProjectFlow.SharedJson, false, [])).ToArray();
        var page = queries.Execute(GenerationVisibilityOwnerProjectFlow.Reader,
            new(partition, Sql, AllowFullScan: true), cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await PointsAsync(database, partition, visible, ownRevision, sharedRevision, token);
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    private static async Task PointsAsync(DatabaseEngine database, PartitionRef partition, string[] visible,
        long ownRevision, long sharedRevision, CancellationToken token)
    {
        foreach (var id in new[] { GenerationVisibilityOwnerProjectFlow.Own,
            GenerationVisibilityOwnerProjectFlow.Shared, GenerationVisibilityOwnerProjectFlow.Hidden })
        {
            var reference = new EntityRef(partition, GenerationVisibilityOwnerProjectFlow.Collection, id);
            var actual = database.GetDocument(GenerationVisibilityOwnerProjectFlow.Reader, reference, cancellationToken: token);
            DocumentResult? expected = visible.Contains(id, StringComparer.Ordinal)
                ? new(reference, id == GenerationVisibilityOwnerProjectFlow.Own ? ownRevision : sharedRevision,
                    id == GenerationVisibilityOwnerProjectFlow.Own ? GenerationVisibilityOwnerProjectFlow.OwnJson
                        : GenerationVisibilityOwnerProjectFlow.SharedJson, false, []) : null;
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        }
    }

    internal static async Task RevokedAsync(DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        PartitionRef partition, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var queryFailure = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(GenerationVisibilityOwnerProjectFlow.Reader,
            new(partition, Sql, AllowFullScan: true), cancellationToken: token));
        await Assert.That(queryFailure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        var pointFailure = Assert.ThrowsExactly<KeyLoadException>(() => database.GetDocument(GenerationVisibilityOwnerProjectFlow.Reader,
            new(partition, GenerationVisibilityOwnerProjectFlow.Collection, GenerationVisibilityOwnerProjectFlow.Own), cancellationToken: token));
        await Assert.That(pointFailure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal static async Task ReceiptAsync(ZoneTreeStore store, CommandRequest command,
        CommitReceipt receipt, MutationReceipt[] expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(store.Position);

        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
