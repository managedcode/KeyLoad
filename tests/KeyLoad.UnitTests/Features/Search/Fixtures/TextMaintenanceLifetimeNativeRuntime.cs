using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using K = KeyLoad.UnitTests.Features.Search.TextMaintenanceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextMaintenanceLifetimeNativeRuntime
{
    private readonly ServiceProvider services;
    internal ITextProjection Projection
    {
        get;
    }
    internal NativeTextIncrementalMaintenanceService Explicit
    {
        get;
    }
    internal NativeTextOnlineMaintenanceService Online
    {
        get;
    }
    internal ServerRuntimeOptions Options
    {
        get;
    }
    internal Guid SessionId { get; } = Guid.NewGuid();

    internal TextMaintenanceLifetimeNativeRuntime(NativeLocalChildLifetimeSeeded state)
    {
        var database = state.Leader.Journal.Database;
        var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
        registrations.AddSingleton(UnitExecutionOptions.DatabaseLimits(database.Limits));
        services = registrations.BuildServiceProvider();
        try
        {
            Options = services.GetRequiredService<ServerRuntimeOptions>();
            var actual = NativeTextHostSearch.Open(database,
                Path.Combine(state.Leader.Journal.Configuration.Directory, K.RootDirectory), Options, database.EvaluationClock);
            Projection = actual.Projection;
            Explicit = actual.Explicit;
            Online = actual.Online;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(services.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task CloseAsync(List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(() => Explicit.AbortAsync(SessionId), failures);
        await ServerFailureObserver.ObserveAsync(() => Online.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => Explicit.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(Projection.Dispose, failures);
        await ServerFailureObserver.ObserveAsync(() => services.DisposeAsync().AsTask(), failures);
    }
}
