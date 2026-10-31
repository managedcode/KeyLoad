using KeyLoad.Core.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server.Search;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextProjectionLeaseTests
{
    private const string Collection = "execution-text-lease";
    private const string TextPath = "/text";

    [Test]
    public async Task SaturationAndCancellationReleaseLeaseForHealthySearch()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        using var projection = new RuntimeTextProjection(Path.Combine(database.Directory, "execution-text"), database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var scope = CaptureScope(database);
        using (var held = projection.Acquire(scope, new()))
        {
            var saturated = Assert.ThrowsExactly<KeyLoadException>(() =>
                projection.Acquire(scope, new()));
            await Assert.That(saturated.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }

        _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
            new(database.Partition, Collection, TextPath, "needle"), TestContext.Current!.Execution.CancellationToken);
        var cancelled = await CancelActiveVerificationAsync(database, projection);
        await Assert.That(cancelled.CancellationToken.IsCancellationRequested).IsTrue();
        var result = await Assert.That(await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
            new(database.Partition, Collection, TextPath, "needle"),
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(result.Document.Reference.Id).IsEqualTo("one");
    }

    [Test]
    public async Task PublishedReadersOverlapWithoutClosingAnotherLeaseOrRewritingItsManifest()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var publications = 0;
        using var projection = new RuntimeTextProjection(Path.Combine(database.Directory, "execution-text"),
            database.Store.Identity.NodeId, UnitTextOptions.Execution(), faultObserver: stage =>
            {
                if (stage == TextFaultStage.InventoryFlushed)
                {
                    Interlocked.Increment(ref publications);
                }
            });
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new SearchRequest(database.Partition, Collection, TextPath, "needle");
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
        var published = publications;
        var firstBudget = new ReadExecutionBudget();
        var secondBudget = new ReadExecutionBudget();
        using (var first = projection.Acquire(CaptureScope(database), firstBudget))
        using (var second = projection.Acquire(CaptureScope(database), secondBudget))
        {
            first.BeginRecord(original.Document.Reference, original.Document.Revision);
            first.ObserveToken("needle");
            second.BeginRecord(original.Document.Reference, original.Document.Revision);
            second.ObserveToken("needle");
            first.VerifyCandidates(["needle"], [original.Document.Reference], firstBudget);
            first.Dispose();
            var following = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
            await Assert.That(following.Document).IsEqualTo(original.Document);
            second.VerifyCandidates(["needle"], [original.Document.Reference], secondBudget);
        }
        var healthy = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
        await Assert.That(healthy.Document).IsEqualTo(original.Document);
        await Assert.That(publications).IsEqualTo(published);
    }

    [Test]
    public async Task CancellingOnePublishedReaderPreservesAnotherAndFollowingSearch()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        using var projection = new RuntimeTextProjection(Path.Combine(database.Directory, "execution-text"),
            database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new SearchRequest(database.Partition, Collection, TextPath, "needle");
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
        var healthyBudget = new ReadExecutionBudget();
        using var cancellation = new CancellationTokenSource();
        var cancelledBudget = new ReadExecutionBudget(cancellationToken: cancellation.Token);
        using (var healthy = projection.Acquire(CaptureScope(database), healthyBudget))
        using (var cancelled = projection.Acquire(CaptureScope(database), cancelledBudget))
        {
            healthy.BeginRecord(original.Document.Reference, original.Document.Revision);
            healthy.ObserveToken("needle");
            cancelled.BeginRecord(original.Document.Reference, original.Document.Revision);
            cancelled.ObserveToken("needle");
            await cancellation.CancelAsync();
            _ = Assert.ThrowsExactly<OperationCanceledException>(() => cancelled.VerifyCandidates(
                ["needle"], [original.Document.Reference], cancelledBudget));
            cancelled.Dispose();
            healthy.VerifyCandidates(["needle"], [original.Document.Reference], healthyBudget);
        }
        var following = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
        await Assert.That(following.Document).IsEqualTo(original.Document);
    }

    [Test]
    public async Task InvalidatingOneReaderRetainsOthersAndReleasesThePhysicalReservation()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var options = UnitTextOptions.Execution(new() { MaximumGenerations = 1 });
        using var projection = new RuntimeTextProjection(Path.Combine(database.Directory, "execution-text"),
            database.Store.Identity.NodeId, options, resources: new TextResourceOwnership(options));
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new SearchRequest(database.Partition, Collection, TextPath, "needle");
        var token = TestContext.Current!.Execution.CancellationToken;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var original = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
            var budget = new ReadExecutionBudget();
            using (var retained = projection.Acquire(CaptureScope(database), budget))
            using (var invalidated = projection.Acquire(CaptureScope(database), new()))
            {
                retained.BeginRecord(original.Document.Reference, original.Document.Revision);
                retained.ObserveToken("needle");
                var mismatch = Assert.ThrowsExactly<KeyLoadException>(() => invalidated.BeginRecord(
                    original.Document.Reference, checked(original.Document.Revision + 1)));
                await Assert.That(mismatch.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
                invalidated.Dispose();
                var denied = Assert.ThrowsExactly<KeyLoadException>(() =>
                {
                    using var borrowed = projection.Acquire(CaptureScope(database), new());
                });
                await Assert.That(denied.Code).IsEqualTo(ErrorCode.Corruption);
                retained.VerifyCandidates(["needle"], [original.Document.Reference], budget);
            }
            var healthy = await Assert.That(await search.SearchAsync("root", request, token)).HasSingleItem();
            await Assert.That(healthy.Document).IsEqualTo(original.Document);
        }
    }

    private static async Task<OperationCanceledException> CancelActiveVerificationAsync(TestDatabase database,
        RuntimeTextProjection projection)
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(cancellationToken: cancellation.Token);
        using var lease = projection.Acquire(CaptureScope(database), budget);
        var document = database.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(database.Partition, Collection), view.MaximumRangeRecords).Records.Select(record =>
            OrleansSerialization.Deserialize<DocumentRecord>(record.Value.Span)).Single());
        lease.BeginRecord(document.Reference, document.Revision);
        lease.ObserveToken("needle");
        await cancellation.CancelAsync();
        return Assert.ThrowsExactly<OperationCanceledException>(() =>
            lease.VerifyCandidates(["needle"], [document.Reference], budget));
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, TextPath, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }
}
