using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.Search;

internal sealed class NativeAnnCrashRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly NativeAnnMaintenanceService owner;
    internal ServerRuntimeOptions Options => services.GetRequiredService<ServerRuntimeOptions>();
    private readonly Guid session = Guid.NewGuid();
    internal NativeAnnCrashRuntime(string directory, DatabaseEngine database)
    {
        services = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build()).BuildServiceProvider();
        try
        {
            owner = new(directory, database, services.GetRequiredService<ServerRuntimeOptions>(), database.EvaluationClock);
        }
        catch (Exception primary)
        {
            try
            { services.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
    internal Task<AnnMaintenanceCapabilityResult> PhaseAsync(DatabaseEngine database, AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityKind phase, ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null)
    {
        var principal = database.Store.Read(view => database.Principal(view, CrashFixtureValues.Principal, database.EvaluationClock.GetUtcNow()));
        return owner.ExecuteAsync(principal, new(request, session, phase, page, intent), CancellationToken.None);
    }
    public async ValueTask DisposeAsync()
    {
        try
        { await owner.DisposeAsync().ConfigureAwait(false); }
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
