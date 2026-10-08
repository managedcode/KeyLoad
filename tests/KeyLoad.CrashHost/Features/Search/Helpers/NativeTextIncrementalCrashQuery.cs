using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalCrashQuery
{
    internal const string ChangedTerm = "оновлено";
    internal const string HealthyTerm = "продовжено";
    internal const string RemovedUkrainian = "привіт";
    internal const string RemovedEnglish = "hello";
    private const string BootstrapDirectory = "text-query-bootstrap";
    private const int Limit = 2;

    internal static async Task<RankedDocument[]> ExecuteAsync(DatabaseEngine database,
        NativeTextIncrementalMaintenanceService owner, ServerRuntimeOptions options, string projectionRoot,
        TextIndexMaintenanceRequest request, string term)
    {
        RankedDocument[]? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new NativeTextSelectedProjection(new NativeTextProjection(
                Path.Combine(Path.GetDirectoryName(projectionRoot)!, BootstrapDirectory), options.Core.DatabaseLimits,
                database.Store.Identity.NodeId, options.NativeText), owner, owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var engine = new SearchEngine(database, options.Core.QueryExecution, projection);
                result = await engine.SearchAsync(CrashFixtureValues.Principal,
                    new SearchRequest(request.Consumer.Partition, request.Collection, request.Field, term, Limit: Limit,
                        TextIndex: new(request.Consumer, request.IndexGeneration)), CancellationToken.None);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
    }
}
