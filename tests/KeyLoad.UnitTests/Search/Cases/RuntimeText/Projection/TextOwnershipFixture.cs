using KeyLoad.Query;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextOwnershipFixture
{
    internal static string CreateGeneration(TestDatabase database, CancellationToken cancellationToken)
    {
        database.Configure(TextProjectionOwnershipTests.Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(TextProjectionOwnershipTests.Collection, "one", "{\"text\":\"needle\"}"));
        var root = Path.Combine(database.Directory, "execution-text-ownership");
        using (var projection = new RuntimeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitTextOptions.Execution()))
        {
            var request = new SearchRequest(database.Partition, TextProjectionOwnershipTests.Collection,
                TextProjectionOwnershipTests.TextPath, TextProjectionOwnershipTests.Query);
            _ = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).Search("root", request, cancellationToken);
        }
        return Directory.EnumerateDirectories(root)
            .Single(path => Path.GetFileName(path).StartsWith(TextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
    }

    internal static void OpenForRestart(string root, TestDatabase database)
    {
        using var projection = new RuntimeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitTextOptions.Execution());
        throw new InvalidOperationException("A malformed or untracked execution generation was accepted.");
    }
}
