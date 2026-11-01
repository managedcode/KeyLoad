using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextGenerationAdmissionTests
{
    private const string Collection = "text-admission";
    private const string Field = "/text";
    private const string Term = "needle";
    private const int DeadlineSeconds = 10;

    [Test]
    public Task ColdBuildWaitStartsAFreshReadAfterTheCurrentWrite() => RunAsync(cancel: false);

    [Test]
    public Task ColdBuildWaitCancelsWithoutClosingItsOwnerThenHealthyReadCompletes() => RunAsync(cancel: true);

    private static async Task RunAsync(bool cancel)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle original\"}"));
        var cleanup = new TextGenerationFixtureLifetime();
        await cleanup.RunAsync(async () =>
        {
            var root = Path.Combine(database.Directory, "text-admission");
            var projection = cleanup.TrackProjection(new(root, database.Store.Identity.NodeId, UnitTextOptions.Execution()));
            var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var request = new SearchRequest(database.Partition, Collection, Field, Term);
            var blocker = cleanup.TrackLease(projection.Acquire(CaptureScope(database), new()));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(DeadlineSeconds), TimeProvider.System);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var before = database.Store.Position;
            var pending = engine.SearchAsync("root", request, cancellation.Token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(pending.IsCompleted).IsFalse();
                if (cancel)
                {
                    await cancellation.CancelAsync();
                    var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
                    await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
                    await Assert.That(database.Store.Position).IsEqualTo(before);
                }
                else
                {
                    database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle revised\"}"));
                }
            }, failures);
            ServerFailureObserver.Observe(() => cleanup.SettleLease(blocker), failures);
            await ServerFailureObserver.ObserveAsync(() => ObservePendingAsync(pending, cancel, cancellation.Token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            var healthy = await engine.SearchAsync("root", request, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(healthy).HasSingleItem();
            await Assert.That(healthy[0].Document.Revision).IsEqualTo(cancel ? 1L : 2L);
        });
    }

    private static async Task ObservePendingAsync(Task<RankedDocument[]> pending, bool cancel, CancellationToken token)
    {
        try
        {
            var rows = await pending;
            await Assert.That(rows).HasSingleItem();
            await Assert.That(rows[0].Document.Revision).IsEqualTo(2L);
            await Assert.That(rows[0].Document.Json).IsEqualTo("{\"text\":\"needle revised\"}");
        }
        catch (OperationCanceledException failure) when (cancel && failure.CancellationToken == token)
        {
        }
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, Field, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }
}
