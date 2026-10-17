using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupIndexedDocumentDedupAssertions
{
    private const string RootPrincipal = "root";
    private const string SelectLabel = "SELECT * FROM backup_documents WHERE label = '";
    private const string LabelSuffix = "'";
    private const string IndexedAccessPrefix = "index:";

    internal static async Task AssertPreRestoreContent(DatabaseEngine database, PartitionRef partition)
    {
        await AssertBaselineContent(database, partition);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.AmberLabel, []);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.GoldLabel, []);
    }

    private static async Task AssertBaselineContent(DatabaseEngine database, PartitionRef partition)
    {
        await AssertDocument(database, partition, BackupIndexedDocumentDedupFixture.FirstId,
            BackupIndexedDocumentDedupFixture.ReplacedJson, BackupIndexedDocumentDedupFixture.SecondRevision);
        await AssertDocument(database, partition, BackupIndexedDocumentDedupFixture.SecondId,
            BackupIndexedDocumentDedupFixture.SecondJson, BackupIndexedDocumentDedupFixture.FirstRevision);
        await AssertMissing(database, partition, BackupIndexedDocumentDedupFixture.DeletedId);
        await AssertMissing(database, partition, BackupIndexedDocumentDedupFixture.TransientId);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.AlphaLabel, []);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.VioletLabel, [BackupIndexedDocumentDedupFixture.FirstId]);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.CobaltLabel, [BackupIndexedDocumentDedupFixture.SecondId]);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.JadeLabel, []);
    }

    internal static async Task AssertFinalContent(DatabaseEngine database, PartitionRef partition)
    {
        await AssertBaselineContent(database, partition);
        await AssertDocument(database, partition, BackupIndexedDocumentDedupFixture.RestoredId,
            BackupIndexedDocumentDedupFixture.RestoredJson, BackupIndexedDocumentDedupFixture.FirstRevision);
        await AssertDocument(database, partition, BackupIndexedDocumentDedupFixture.HealthyId,
            BackupIndexedDocumentDedupFixture.HealthyJson, BackupIndexedDocumentDedupFixture.FirstRevision);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.AmberLabel, [BackupIndexedDocumentDedupFixture.RestoredId]);
        await AssertIndexed(database, partition, BackupIndexedDocumentDedupFixture.GoldLabel, [BackupIndexedDocumentDedupFixture.HealthyId]);
    }

    internal static async Task AssertDocument(DatabaseEngine database, PartitionRef partition,
        string id, string json, long revision)
    {
        var reference = new EntityRef(partition, BackupIndexedDocumentDedupFixture.Collection, id);
        var document = database.GetDocument(RootPrincipal, reference);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Reference).IsEqualTo(reference);
        await Assert.That(document.Json).IsEqualTo(json);
        await Assert.That(document.Revision).IsEqualTo(revision);
    }

    internal static async Task AssertMissing(DatabaseEngine database, PartitionRef partition, string id)
    {
        var reference = new EntityRef(partition, BackupIndexedDocumentDedupFixture.Collection, id);
        await Assert.That(database.GetDocument(RootPrincipal, reference)).IsNull();
    }

    internal static async Task AssertIndexed(DatabaseEngine database, PartitionRef partition,
        string label, string[] expectedIds)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var page = engine.Execute(RootPrincipal, new QueryRequest(partition, SelectLabel + label + LabelSuffix));
        await Assert.That(page.AccessPath).IsEqualTo(IndexedAccessPrefix + BackupIndexedDocumentDedupFixture.IndexName);
        await Assert.That(page.Rows.Select(row => row.EntityId)).IsEquivalentTo(expectedIds);
    }

    internal static async Task AssertOldOutcomePreserved(IAtomicStore store, PartitionRef partition,
        Guid commandId, byte[] originalBytes)
    {
        var current = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(store, partition, RootPrincipal, commandId);
        await Assert.That(current.AsSpan().SequenceEqual(originalBytes)).IsTrue();
    }

    internal static Dictionary<Guid, byte[]> CaptureCurrentOutcomes(IAtomicStore store, PartitionRef partition,
        Guid restoredCommandId, Guid healthyCommandId)
        => new()
        {
            [restoredCommandId] = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(store, partition, RootPrincipal, restoredCommandId),
            [healthyCommandId] = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(store, partition, RootPrincipal, healthyCommandId)
        };

    internal static async Task AssertCurrentOutcomes(IAtomicStore store, PartitionRef partition,
        IReadOnlyDictionary<Guid, byte[]> expected)
    {
        foreach (var item in expected)
        {
            var actual = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(store, partition, RootPrincipal, item.Key);
            await Assert.That(actual.AsSpan().SequenceEqual(item.Value)).IsTrue();
        }
    }

    internal static async Task AssertArchiveUnchanged(string backup, IReadOnlyDictionary<string, byte[]> original)
    {
        var currentFiles = Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories).ToArray();
        await Assert.That(currentFiles.Length).IsEqualTo(original.Count);
        foreach (var path in currentFiles)
        {
            var relative = Path.GetRelativePath(backup, path);
            await Assert.That(original.ContainsKey(relative)).IsTrue();
            var current = await File.ReadAllBytesAsync(path);
            await Assert.That(current.AsSpan().SequenceEqual(original[relative])).IsTrue();
        }
    }
}
