using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextBilingualAudit
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
    private const string ProjectionDirectory = "bilingual-native-text";
    private const int Limit = 2;
    private const long Revision = 1;
    private const double SingleBranchScore = 1d / 61;

    internal static SearchRequest Request(PartitionRef partition, string text)
        => new(partition, Collection, TextPath, text, Limit: Limit);
    internal static NativeTextProjection Open(TestDatabase database)
        => new(Path.Combine(database.Directory, ProjectionDirectory), UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
    internal static DatabaseEngine Owner(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution());
    internal static async Task VerifyAsync(SearchEngine search, PartitionRef partition, CancellationToken token)
    {
        await LiteralAsync(search, partition, UkrainianQuery, UkrainianId, UkrainianJson, token);
        await LiteralAsync(search, partition, EnglishQuery, EnglishId, EnglishJson, token);
    }
    internal static async Task VerifyDeletedAsync(SearchEngine search, PartitionRef partition, CancellationToken token)
    {
        await Assert.That(await search.SearchAsync(Root, Request(partition, UkrainianQuery), token)).IsEmpty();
        await LiteralAsync(search, partition, EnglishQuery, EnglishId, EnglishJson, token);
    }
    private static async Task LiteralAsync(SearchEngine search, PartitionRef partition, string query, string id,
        string json, CancellationToken token)
    {
        var result = await search.SearchAsync(Root, Request(partition, query), token);
        var row = await Assert.That(result).HasSingleItem();
        var expected = new DocumentResult(new(partition, Collection, id), Revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(row.Document).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(row.Score).IsEqualTo(SingleBranchScore);
    }
}
