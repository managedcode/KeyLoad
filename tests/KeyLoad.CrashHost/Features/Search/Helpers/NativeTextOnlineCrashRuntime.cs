using KeyLoad.Orleans.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

internal sealed class NativeTextOnlineCrashRuntime : IAsyncDisposable
{
    private const int OriginalStorageOwnerIndex = 0;
    private readonly NativeTextIncrementalMaintenanceService explicitOwner;
    private readonly ITextProjection projection;
    internal NativeTextIncrementalCrashRuntime Canonical { get; }
    internal NativeTextOnlineMaintenanceService Online { get; }
    internal SearchEngine Search { get; }
    internal string PhysicalRoot { get; }

    internal NativeTextOnlineCrashRuntime(string root, Guid incarnation, Action<NativeTextFaultStage>? observer)
    {
        Canonical = new(root, incarnation);
        try
        {
            PhysicalRoot = Path.GetDirectoryName(ReplicaCrashNode.TargetStoreDirectories(root)[OriginalStorageOwnerIndex])
                ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
            var opened = NativeTextHostSearch.Open(Canonical.Database, PhysicalRoot, Canonical.Options,
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
        OnlineTextCapabilityKind kind, DateTimeOffset expiry, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, ProjectionBatchResult? receipt = null)
    {
        var database = Canonical.Database;
        var principal = database.Store.Read(view => database.Principal(view, CrashFixtureValues.Principal,
            database.EvaluationClock.GetUtcNow()));
        return Online.ExecuteAsync(principal, new(session, request, kind, page, intent, receipt), expiry, token);
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
