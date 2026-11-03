using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextGenerationPublishAdmissionTests
{
    private const string Collection = "text-publish-admission";
    private const string Field = "/text";
    private const string Term = "needle";
    private const string Original = "{\"text\":\"needle original\"}";
    private const string Middle = "{\"text\":\"needle middle\"}";
    private const string Latest = "{\"text\":\"needle latest\"}";

    [Test]
    public async Task AReaderJoiningTheCurrentGenerationDuringBuildWaitsForPublicationCapacity()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", Original));
        var cleanup = new TextGenerationFixtureLifetime();
        await cleanup.RunAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), TimeProvider.System);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var token = cancellation.Token;
            var armed = false;
            TextProjectionScope? currentScope = null;
            ITextProjectionLease? lateReader = null;
            RuntimeTextProjection? projection = null;
            projection = cleanup.TrackProjection(new(Path.Combine(database.Directory, "publish-admission"),
                database.Store.Identity.NodeId, UnitTextOptions.Execution(), new TextResourceOwnership(UnitTextOptions.Execution()), faultObserver: stage =>
                {
                    if (stage == TextFaultStage.PostingWritten && armed)
                    {
                        armed = false;
                        lateReader = cleanup.TrackLease(projection!.Acquire(currentScope!, new()));
                    }
                }));
            var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var request = new SearchRequest(database.Partition, Collection, Field, Term);
            _ = await engine.SearchAsync("root", request, token);
            var oldest = cleanup.TrackLease(projection.Acquire(CaptureScope(database), new()));
            database.Commit(new PutDocument(Collection, "one", Middle));
            _ = await engine.SearchAsync("root", request, token);
            currentScope = CaptureScope(database);
            database.Commit(new PutDocument(Collection, "one", Latest));
            armed = true;
            var pending = engine.SearchAsync("root", request, token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(lateReader).IsNotNull();
                await Assert.That(pending.IsCompleted).IsFalse();
            }, failures);
            ServerFailureObserver.Observe(() => cleanup.SettleLease(oldest), failures);
            if (lateReader is not null)
            {
                ServerFailureObserver.Observe(() => cleanup.SettleLease(lateReader), failures);
            }
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AssertLatestAsync(await pending.WaitAsync(token));
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            await AssertLatestAsync(await engine.SearchAsync("root", request, token));
        });
    }

    private static async Task AssertLatestAsync(RankedDocument[] rows)
    {
        await Assert.That(rows).HasSingleItem();
        await Assert.That(rows[0].Document.Revision).IsEqualTo(3L);
        await Assert.That(rows[0].Document.Json).IsEqualTo(Latest);
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, Field, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }
}
