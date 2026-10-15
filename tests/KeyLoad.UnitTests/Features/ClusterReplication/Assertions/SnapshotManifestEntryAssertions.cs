using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryAssertions
{
    internal static string[] Bytes(ZoneTreeStore store)
    {
        var page = store.Read(view => view.Scan([], P.RecordLimit));
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete snapshot fixture exceeded its native record bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    }
    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task DocumentAsync(ReplicaCrashNode node, int cut)
    {
        DocumentResult expected = new(ReplicaCrashModel.Document, cut - P.RevisionOffset,
            cut == P.HealthyCut ? P.HealthyJson : ReplicaCrashModel.JsonAt(cut), false, []);
        await SameAsync(expected, node.Database.GetDocument(ReplicaCrashModel.PrincipalId, ReplicaCrashModel.Document)!);
        await Assert.That(node.Database.LastApplied).IsEqualTo((long)cut);
    }
    internal static async Task ReceiptAsync(ReplicaCrashNode node, ReplicatedOperation operation, int cut)
    {
        var actual = OutcomeStoreOracle.Read(node.Canonical, operation)!.Get<CommitReceipt>();
        CommitReceipt expected = new(operation.Id, new(node.Configuration.Incarnation,
            ReplicaCrashModel.Document.Partition.AtomicPartitionId, cut, P.OwnershipEpoch),
            [new(P.Put, P.Collection, P.Document, cut - P.RevisionOffset)], node.Canonical.Identity.Durability);
        await SameAsync(expected, actual);
        await Assert.That(actual.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
        var before = Bytes(node.Canonical);
        var position = node.Canonical.Position;
        var raw = node.Canonical.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            ReplicaCrashModel.Document.Partition, operation.PrincipalId, operation.Id)))!;
        var replay = node.Database.Apply(operation).Get<CommitReceipt>();
        await SameAsync(expected, replay);
        await Assert.That(replay.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
        await Assert.That(node.Canonical.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            ReplicaCrashModel.Document.Partition, operation.PrincipalId, operation.Id)))!.AsSpan().SequenceEqual(raw)).IsTrue();
        await Assert.That(Bytes(node.Canonical).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        await Assert.That(node.Canonical.Position).IsEqualTo(position);
    }
    internal static async Task AllReceiptsAsync(ReplicaCrashNode node, ReplicatedOperation healthy)
    {
        for (var index = P.FirstDocument; index <= P.TailCut; index++)
        {
            await ReceiptAsync(node, ReplicaCrashModel.Operation(index), index);
        }
        await ReceiptAsync(node, healthy, P.HealthyCut);
    }
}
