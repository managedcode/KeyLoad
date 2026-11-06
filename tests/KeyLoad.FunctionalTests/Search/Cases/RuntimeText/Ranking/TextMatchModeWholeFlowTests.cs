using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextMatchModeWholeFlowTests
{
    private const string Collection = "text-match-mode";
    private const string Reader = "text-match-reader";
    private const string Owner = "text-match-owner";
    private const string PartialId = "partial-a";
    private const string CompleteId = "complete";
    private const string PartialJson = "{\"text\":\"alpha\"}";
    private static readonly string CompleteJson = "{\"text\":\"alpha beta "
        + string.Join(' ', Enumerable.Repeat("padding", 64)) + "\"}";

    [Test]
    public async Task AllTermsExcludeHigherRankedPartialRowsBeforeLimitAndPreservePrivacyThroughColdRead()
    {
        using var database = new TestDatabase();
        Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = UnitTextOptions.Execution();
        var root = Path.Combine(database.Directory, Collection);
        var node = database.Store.Identity.NodeId;
        using var projection = new RuntimeTextProjection(root, node, options, new TextResourceOwnership(options));
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var any = new SearchRequest(database.Partition, Collection, "/text", "alpha beta", Limit: 1);
        var all = any with { TextMatchMode = TextMatchMode.All };
        var position = database.Store.Position;
        await RequireAsync(database, engine, any, PartialId, PartialJson, token);
        await RequireAsync(database, engine, all, CompleteId, CompleteJson, token);
        await RequireAsync(database, engine, all with { Text = "ＡＬＰＨＡ alpha BeTa beta" }, CompleteId, CompleteJson, token);
        await Assert.That(await engine.SearchAsync(Reader, all with { Text = "alpha beta absent" }, token)).IsEmpty();
        await Assert.That(await engine.SearchAsync(Reader, all with { Text = string.Empty }, token)).IsEmpty();
        await RefusalsAsync(database, engine, all, position, token);
        await RequireCorpusStatisticsAsync(database, all, token);
        var json = JsonDefaults.Serialize(all);
        await RequireAsync(database, engine, JsonDefaults.Deserialize<SearchRequest>(json), CompleteId, CompleteJson, token);
        var binary = OrleansSerialization.Serialize(all);
        await RequireAsync(database, engine, OrleansSerialization.Deserialize<SearchRequest>(binary), CompleteId, CompleteJson, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        projection.Dispose();
        using var cold = new RuntimeTextProjection(root, node, options, new TextResourceOwnership(options));
        var reopened = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), cold);
        await RequireAsync(database, reopened, all, CompleteId, CompleteJson, token);
        await RequireAsync(database, reopened, any, PartialId, PartialJson, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static void Seed(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, PartialId, PartialJson, Access: new(Owner)),
            new PutDocument(Collection, CompleteId, CompleteJson, Access: new(Owner)),
            new PutDocument(Collection, "partial-z", "{\"text\":\"beta\"}", Access: new(Owner)),
            new PutDocument(Collection, "private", "{\"text\":\"alpha beta\"}", Access: new("another-owner")));
        var principal = new PrincipalRecord(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], [])
        {
            OwnerId = Owner,
            RestrictRows = true,
            PolicyEpoch = 1
        };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    private static async Task RequireCorpusStatisticsAsync(TestDatabase database, SearchRequest request, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(token);
        var observed = database.Database.WithQueryView(Reader, request.Partition, request.Collection, Capability.Query,
            (view, principal, _) =>
            {
                var execution = UnitExecutionOptions.QueryExecution().Value;
                var ranker = new TextRanker(request.Text!, request.TextField!, budget, execution.TextBudgetCheckInterval,
                    execution.MaximumDocumentWords, execution.MaximumWordCharacters, request.TextMatchMode);
                database.Database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, ranker.Visit);
                return (Statistics: ranker.CaptureStatistics(), Scores: ranker.Rank());
            }, budget);
        await Assert.That(observed.Statistics.DocumentCount).IsEqualTo(3);
        await Assert.That(observed.Statistics.TotalLength).IsEqualTo(68L);
        await Assert.That(observed.Statistics.DocumentFrequencies).IsEquivalentTo(new[] { 2, 2 }, CollectionOrdering.Matching);
        var score = await Assert.That(observed.Scores).HasSingleItem();
        await Assert.That(score.Reference).IsEqualTo(database.ExpectedReference(new(database.Partition, Collection, CompleteId)));
        await Assert.That(score.Score).IsEqualTo(0.5274759409989652d).Within(0.000000000001d);
    }

    private static async Task RefusalsAsync(TestDatabase database, SearchEngine engine,
        SearchRequest all, long position, CancellationToken token)
    {
        foreach (var invalid in new[] { all with { TextMatchMode = (TextMatchMode)99 }, all with { Text = null } })
        {
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchAsync(Reader, invalid, token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await RequireAsync(database, engine, all, CompleteId, CompleteJson, token);
        }
    }

    private static async Task RequireAsync(TestDatabase database, SearchEngine engine,
        SearchRequest request, string id, string json, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(database.ExpectedReference(new(database.Partition, Collection, id)),
            1, json, false, []), 1d / 61d)];
        await Assert.That(JsonDefaults.Serialize(await engine.SearchAsync(Reader, request, token)))
            .IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
    }
}
