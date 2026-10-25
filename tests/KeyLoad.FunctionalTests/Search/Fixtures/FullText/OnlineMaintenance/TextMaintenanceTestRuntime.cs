using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextMaintenanceTestRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
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
            Owner = new(database, directory, database.Store.Identity.NodeId,
                services.GetRequiredService<ServerRuntimeOptions>(), phaseClock ?? database.EvaluationClock);
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
        try
        {
            await Owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            try
            {
                await services.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        await services.DisposeAsync().ConfigureAwait(false);
    }
}
