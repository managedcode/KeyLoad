using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Search;

internal sealed class AnnMaintenanceTestRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    internal AnnMaintenanceService Owner
    {
        get;
    }
    internal CommitToken? VerifiedCanonicalCut { get; private set; }
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal AnnMaintenanceTestRuntime(TestDatabase database, TimeProvider? phaseClock = null, IConfiguration? configuration = null)
        : this(database.Database, database.Directory, phaseClock, configuration)
    {
    }

    internal AnnMaintenanceTestRuntime(DatabaseEngine database, string directory,
        TimeProvider? phaseClock = null, IConfiguration? configuration = null)
    {
        var registrations = new ServiceCollection().AddRuntimeOptions(configuration ?? new ConfigurationBuilder().Build());
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
    internal async Task<AnnMaintenanceCapabilityResult> PhaseAsync(TestDatabase database, AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityKind phase, ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null,
        CancellationToken? token = null)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view,
            AnnProjectionPinTestSupport.Principal, database.Database.EvaluationClock.GetUtcNow()));
        CommitToken? expected = phase == AnnMaintenanceCapabilityKind.Verify
            ? new(database.Store.Identity.Incarnation, request.Consumer.Partition.AtomicPartitionId, database.Database.LastApplied)
            : null;
        var result = await Owner.ExecuteAsync(principal, new(request, SessionId, phase, page, intent),
            token ?? TestContext.Current!.Execution.CancellationToken);
        if (expected is not null)
        {
            VerifiedCanonicalCut = expected;
        }
        return result;
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
