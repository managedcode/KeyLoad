using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnMaintenanceTestRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    internal NativeAnnMaintenanceService Owner
    {
        get;
    }
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal NativeAnnMaintenanceTestRuntime(TestDatabase database, TimeProvider? phaseClock = null, IConfiguration? configuration = null)
        : this(database.Database, database.Directory, phaseClock, configuration)
    {
    }

    internal NativeAnnMaintenanceTestRuntime(DatabaseEngine database, string directory,
        TimeProvider? phaseClock = null, IConfiguration? configuration = null)
    {
        var registrations = new ServiceCollection().AddRuntimeOptions(configuration ?? new ConfigurationBuilder().Build());
        registrations.AddSingleton(database.OperationLimitsOptions);
        services = registrations.BuildServiceProvider();
        try
        {
            Owner = new(directory, database, services.GetRequiredService<ServerRuntimeOptions>(), phaseClock ?? database.EvaluationClock);
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
    internal Task<AnnMaintenanceCapabilityResult> PhaseAsync(TestDatabase database, AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityKind phase, ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null,
        CancellationToken? token = null)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view,
            AnnProjectionPinTestSupport.Principal, database.Database.EvaluationClock.GetUtcNow()));
        return Owner.ExecuteAsync(principal, new(request, SessionId, phase, page, intent), token ?? TestContext.Current!.Execution.CancellationToken);
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
