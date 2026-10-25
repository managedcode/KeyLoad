using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;

namespace KeyLoad.RecoveryTests.Search;

internal static class TextIncrementalCrashQuery
{
    internal const string ChangedTerm = "оновлено";
    internal const string HealthyTerm = "продовжено";
    internal const string RemovedUkrainian = "привіт";
    internal const string RemovedEnglish = "hello";
    private const string BootstrapDirectory = "text-query-bootstrap";
    private const int Limit = 2;

    internal static async Task<RankedDocument[]> ExecuteAsync(DatabaseEngine database,
        TextIncrementalMaintenanceService owner, ServerRuntimeOptions options, string projectionRoot,
        TextIndexMaintenanceRequest request, string term)
    {
        RankedDocument[]? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new TextSelectedProjection(new RuntimeTextProjection(
                Path.Combine(Path.GetDirectoryName(projectionRoot)!, BootstrapDirectory),
                database.Store.Identity.NodeId, options.RuntimeText), owner, owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var engine = new SearchEngine(database, options.Core.QueryExecution, projection);
                result = await engine.SearchAsync(CrashFixtureValues.Principal,
                    new SearchRequest(request.Consumer.Partition, request.Collection, request.Field, term, Limit: Limit,
                        TextIndex: new(request.Consumer, request.IndexGeneration)), CancellationToken.None);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
    }
}
