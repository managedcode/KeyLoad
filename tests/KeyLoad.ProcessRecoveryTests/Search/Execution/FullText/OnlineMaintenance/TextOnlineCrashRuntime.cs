using KeyLoad.Clustering.Search;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal sealed class TextOnlineCrashRuntime : IAsyncDisposable
{
    private readonly TextIncrementalMaintenanceService explicitOwner;
    private readonly ITextProjection projection;
    internal TextIncrementalCrashRuntime Canonical
    {
        get;
    }
    internal TextOnlineMaintenanceService Online
    {
        get;
    }
    internal SearchEngine Search
    {
        get;
    }
    internal string PhysicalRoot
    {
        get;
    }

    internal TextOnlineCrashRuntime(string root, Guid incarnation, Action<TextFaultStage>? observer)
    {
        Canonical = new(root, incarnation);
        try
        {
            PhysicalRoot = Canonical.PhysicalRoot;
            var opened = TextHostSearch.Open(Canonical.Database, PhysicalRoot, Canonical.Options,
                Canonical.Database.EvaluationClock, observer);
            Online = opened.Online;
            explicitOwner = opened.Explicit;
            projection = opened.Projection;
            Search = new(Canonical.Database, Canonical.Options.Core.QueryExecution, projection);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => Canonical.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<OnlineTextCapabilityResult> PhaseAsync(Guid session, OnlineTextIndexMaintenanceRequest request,
        OnlineTextCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, ProjectionBatchResult? receipt = null)
    {
        var database = Canonical.Database;
        var principal = database.Store.Read(view => database.Principal(view, CrashFixtureValues.Principal,
            database.EvaluationClock.GetUtcNow()));
        return Online.ExecuteAsync(principal, new(session, request, kind, page, intent, receipt), token);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Online.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => explicitOwner.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(projection.Dispose, failures);
        await ServerFailureObserver.ObserveAsync(() => Canonical.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
