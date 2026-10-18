using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Builds one production application without acquiring its physical storage.</summary>
internal static class ControlledPartitionMovementNativeApplicationFactory
{
    internal static async Task<ControlledPartitionMovementNativeHost> CreateAsync(string[] arguments,
        NativeLocalhostClusteringOptions localhostClustering,
        IDatabaseAuthenticationExecutionObserver? authenticationObserver,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory,
        IGrainRequestPhaseObserver? phaseObserver, IGrainRequestPurposeObserver? purposeObserver,
        Action<IServiceCollection, NodeOptions>? configureServices, CancellationToken startupCancellation)
    {
        var application = await ServerConfiguration.BuildAsync(arguments,
            localhostClustering: localhostClustering, authenticationExecutionObserver: authenticationObserver,
            lifecycleParticipantFactory: lifecycleParticipantFactory,
            phaseObserver: phaseObserver, purposeObserver: purposeObserver, configureServices: configureServices,
            startupCancellation: startupCancellation).ConfigureAwait(false);
        try
        {
            return new(application);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
