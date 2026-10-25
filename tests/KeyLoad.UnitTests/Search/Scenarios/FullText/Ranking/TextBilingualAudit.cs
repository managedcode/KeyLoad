using KeyLoad.Core.Authorization;
using KeyLoad.Query;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Search;

internal static class TextBilingualAudit
{
    internal const string Collection = "bilingual-provider-audit";
    internal const string UkrainianId = "ukrainian";
    internal const string EnglishId = "english";
    internal const string UkrainianJson = """{"text":"привіт світ"}""";
    internal const string EnglishJson = """{"text":"hello world"}""";
    internal const string EnglishQuery = "hello";
    internal const string Denied = "unknown-bilingual-reader";
    private const string UkrainianQuery = "ПРИВІТ";
    private const string TextPath = "/text";
    private const string Root = "root";
    private const string ProjectionDirectory = "bilingual-execution-text";
    private const int Limit = 2;
    private const long Revision = 1;
    private const double SingleBranchScore = 1d / 61;

    internal static SearchRequest Request(PartitionRef partition, string text)
        => new(partition, Collection, TextPath, text, Limit: Limit);
    internal static RuntimeTextProjection Open(TestDatabase database)
        => new(Path.Combine(database.Directory, ProjectionDirectory),
            database.Store.Identity.NodeId, UnitTextOptions.Execution());
    internal static DatabaseEngine Owner(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution());
    internal static async Task VerifyAsync(SearchEngine search, TestDatabase database, CancellationToken token)
    {
        await LiteralAsync(search, database, UkrainianQuery, UkrainianId, UkrainianJson, token);
        await LiteralAsync(search, database, EnglishQuery, EnglishId, EnglishJson, token);
    }
    internal static async Task VerifyDeletedAsync(SearchEngine search, TestDatabase database, CancellationToken token)
    {
        await Assert.That(await search.SearchAsync(Root, Request(database.Partition, UkrainianQuery), token)).IsEmpty();
        await LiteralAsync(search, database, EnglishQuery, EnglishId, EnglishJson, token);
    }
    private static async Task LiteralAsync(SearchEngine search, TestDatabase database, string query, string id,
        string json, CancellationToken token)
    {
        var result = await search.SearchAsync(Root, Request(database.Partition, query), token);
        var row = await Assert.That(result).HasSingleItem();
        var expected = new DocumentResult(database.ExpectedReference(new(database.Partition, Collection, id)), Revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(row.Document).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(row.Score).IsEqualTo(SingleBranchScore);
    }
}
