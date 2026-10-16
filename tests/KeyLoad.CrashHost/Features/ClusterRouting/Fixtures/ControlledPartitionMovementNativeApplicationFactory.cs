using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Builds one production application without acquiring its physical storage.</summary>
internal static class ControlledPartitionMovementNativeApplicationFactory
{
    internal static async Task<ControlledPartitionMovementNativeHost> CreateAsync(string[] arguments,
        NativeLocalhostClusteringOptions localhostClustering, IGrainPartitionMovementSealedOperationObserver? sealedObserver,
        IDatabaseAuthenticationExecutionObserver? authenticationObserver,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory,
        IGrainRequestPhaseObserver? phaseObserver, IGrainRequestPurposeObserver? purposeObserver,
        CancellationToken startupCancellation)
    {
        var nativeObservation = new ControlledPartitionMovementNativeObservationWitness();
        var application = await ServerConfiguration.BuildAsync(arguments, sealedObserver: sealedObserver,
            localhostClustering: localhostClustering, authenticationExecutionObserver: authenticationObserver,
            partitionMovementNativeObserver: nativeObservation, lifecycleParticipantFactory: lifecycleParticipantFactory,
            phaseObserver: phaseObserver, purposeObserver: purposeObserver, startupCancellation: startupCancellation).ConfigureAwait(false);
        try
        {
            return new(application, nativeObservation);
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
