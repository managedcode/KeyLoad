using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server.Search;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextGenerationRetirementTests
{
    private const string Collection = "text-retirement-publication";
    private const string Field = "/text";
    private const string Term = "needle";
    private const string Revised = """{"text":"needle revised"}""";

    [Test]
    public async Task CurrentSearchRemainsReadableAtTheRetiredDirectoryDeletionBoundary()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", """{"text":"needle original"}"""));
        var cleanup = new TextGenerationFixtureLifetime();
        await cleanup.RunAsync(async () =>
        {
            var root = Path.Combine(database.Directory, "text-retirement-publication");
            var token = TestContext.Current!.Execution.CancellationToken;
            var request = new SearchRequest(database.Partition, Collection, Field, Term);
            SearchEngine? engine = null;
            Task<RankedDocument[]>? overlapping = null;
            var observed = 0;
            var projection = cleanup.TrackProjection(new(root, database.Store.Identity.NodeId, UnitTextOptions.Execution(),
                faultObserver: stage =>
                {
                    if (stage == TextFaultStage.RetiredGenerationDeleted)
                    {
                        observed++;
                        overlapping = engine!.SearchAsync("root", request, token);
                    }
                }));
            engine = new(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var original = await engine.SearchAsync("root", request, token);
            var retired = GenerationPaths(root).Single();
            var oldBudget = new ReadExecutionBudget();
            var retained = cleanup.TrackLease(projection.Acquire(CaptureScope(database), oldBudget));
            retained.BeginRecord(original[0].Document.Reference, original[0].Document.Revision);
            retained.ObserveToken(Term);
            database.Commit(new PutDocument(Collection, "one", Revised));
            var replacement = await engine.SearchAsync("root", request, token);
            retained.VerifyCandidates([Term], [original[0].Document.Reference], oldBudget);
            cleanup.SettleLease(retained);
            await Assert.That(observed).IsEqualTo(1);
            var actual = await (overlapping ?? throw new InvalidOperationException("The retirement boundary did not start its current read."));
            await Assert.That(actual).HasSingleItem();
            await Assert.That(actual[0].Document.Revision).IsEqualTo(2L);
            await Assert.That(actual[0].Document.Json).IsEqualTo(Revised);
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(replacement))).IsTrue();
            await Assert.That(Directory.Exists(retired)).IsFalse();
            await Assert.That(GenerationPaths(root).Length).IsEqualTo(1);
            var healthy = await engine.SearchAsync("root", request, token);
            await Assert.That(JsonDefaults.Serialize(healthy).AsSpan().SequenceEqual(JsonDefaults.Serialize(replacement))).IsTrue();
        });
    }

    private static string[] GenerationPaths(string root)
        => Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();

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
