using KeyLoad.Query;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextOwnershipFixture
{
    internal static string CreateGeneration(TestDatabase database, CancellationToken cancellationToken)
    {
        database.Configure(NativeTextProjectionOwnershipTests.Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(NativeTextProjectionOwnershipTests.Collection, "one", "{\"text\":\"needle\"}"));
        var root = Path.Combine(database.Directory, "native-text-ownership");
        using (var projection = new NativeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution()))
        {
            var request = new SearchRequest(database.Partition, NativeTextProjectionOwnershipTests.Collection,
                NativeTextProjectionOwnershipTests.TextPath, NativeTextProjectionOwnershipTests.Query);
            _ = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).Search("root", request, cancellationToken);
        }
        return Directory.EnumerateDirectories(root)
            .Single(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
    }

    internal static void OpenForRestart(string root, TestDatabase database)
    {
        using var projection = new NativeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        throw new InvalidOperationException("A malformed or untracked native generation was accepted.");
    }
}
