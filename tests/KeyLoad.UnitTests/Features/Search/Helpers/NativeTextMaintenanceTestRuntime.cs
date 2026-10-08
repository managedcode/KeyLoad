using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextMaintenanceTestRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    internal NativeTextIncrementalMaintenanceService Owner { get; }
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal NativeTextMaintenanceTestRuntime(TestDatabase database, TimeProvider? phaseClock = null)
    {
        var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
        registrations.AddSingleton(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        services = registrations.BuildServiceProvider();
        try
        {
            Owner = new(database.Database, database.Directory, database.Store.Identity.NodeId,
                services.GetRequiredService<ServerRuntimeOptions>(), phaseClock ?? database.Database.EvaluationClock);
        }
        catch (Exception primary)
        {
            try
            { services.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
    internal Task<TextMaintenanceCapabilityResult> PhaseAsync(TestDatabase database, TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityKind phase, ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null,
        ProjectionBatchResult? acknowledged = null, CancellationToken? token = null)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view,
            NativeTextMaintenanceTestValues.Principal, database.Database.EvaluationClock.GetUtcNow()));
        return Owner.ExecuteAsync(principal, new(SessionId, request, phase, page, intent, acknowledged), token ?? TestContext.Current!.Execution.CancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        try
        { await Owner.DisposeAsync().ConfigureAwait(false); }
        catch (Exception primary)
        {
            try
            { await services.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        await services.DisposeAsync().ConfigureAwait(false);
    }
}
