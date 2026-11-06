using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextMaintenanceTestRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly ITextProjection projection;
    private readonly TextOnlineMaintenanceService online;
    internal SearchEngine Search { get; }
    internal TextIncrementalMaintenanceService Owner
    {
        get;
    }
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal TextMaintenanceTestRuntime(TestDatabase database, TimeProvider? phaseClock = null)
        : this(database.Database, database.Directory, phaseClock)
    {
    }

    internal TextMaintenanceTestRuntime(DatabaseEngine database, string directory, TimeProvider? phaseClock = null)
    {
        var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
        services = registrations.BuildServiceProvider();
        try
        {
            var options = services.GetRequiredService<ServerRuntimeOptions>();
            var actual = TextHostSearch.Open(database, directory, options, phaseClock ?? database.EvaluationClock);
            Owner = actual.Explicit;
            online = actual.Online;
            projection = actual.Projection;
            Search = new(database, options.Core.QueryExecution, projection);
        }
        catch (Exception primary)
        {
            try
            {
                services.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
    internal Task<TextMaintenanceCapabilityResult> PhaseAsync(TestDatabase database, TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityKind phase, ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null,
        ProjectionBatchResult? acknowledged = null, CancellationToken? token = null)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view,
            TextMaintenanceTestValues.Principal, database.Database.EvaluationClock.GetUtcNow()));
        return Owner.ExecuteAsync(principal, new(SessionId, request, phase, page, intent, acknowledged), token ?? TestContext.Current!.Execution.CancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => online.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => Owner.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(projection.Dispose, failures);
        try
        {
            await services.DisposeAsync();
        }
        catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
