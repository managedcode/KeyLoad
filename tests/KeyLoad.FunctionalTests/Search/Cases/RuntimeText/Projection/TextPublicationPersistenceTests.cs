using KeyLoad.Query;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextPublicationPersistenceTests
{
    [Test]
    public async Task FailedNativeMergeRefusesPublicationBeforeHealthySearchAndColdPostings()
    {
        using var database = new TestDatabase();
        database.Configure(TextPublicationFailureFlow.Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(TextPublicationFailureFlow.Collection, "a", TextPublicationFailureFlow.Payload),
            new PutDocument(TextPublicationFailureFlow.Collection, "b", TextPublicationFailureFlow.Payload));
        var position = database.Store.Position;
        var identity = database.Store.Identity;
        var root = Path.Combine(database.Directory, "publication-close");
        var token = TestContext.Current!.Execution.CancellationToken;
        await TextPublicationFailureFlow.RefuseAsync(database, root, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await HealthyAsync(database.Database, database.Partition, root, identity.NodeId, token);
        database.JoinOwnedResources();
        using var cold = ZoneTreeExistingStore.Open(new(database.Directory) { Incarnation = identity.Incarnation },
            identity.NodeId, database.StorageExecution);
        await HealthyAsync(database.CreateConfiguredDatabase(cold), database.Partition, root, identity.NodeId, token);
        await Assert.That(cold.Position).IsEqualTo(position);
    }

    private static async Task HealthyAsync(DatabaseEngine database, PartitionRef partition, string root, Guid node,
        CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var resources = new TextResourceOwnership(options);
        using (var projection = new RuntimeTextProjection(root, node, options, resources))
        {
            var engine = new SearchEngine(database, UnitExecutionOptions.QueryExecution(), projection);
            var rows = await engine.SearchAsync("root", new(partition,
                TextPublicationFailureFlow.Collection, "/text", "needle"), token);
            await Assert.That(string.Join(',', rows.Select(row => row.Document.Reference.Id))).IsEqualTo("a,b");
            await Assert.That(rows.All(row => row.Document.Json == TextPublicationFailureFlow.Payload
                && row.Document.Revision == 1 && !row.Document.Redacted
                && row.Document.Reference.Partition == partition
                && row.Document.Reference.Collection == TextPublicationFailureFlow.Collection)).IsTrue();
        }
        await TextPublicationFailureFlow.ColdPostingsAsync(root, node, options, resources, token);
    }
}
