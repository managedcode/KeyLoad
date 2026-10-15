using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePreStartDisposeTrial
{
    private const int FirstOwner = 0;

    internal static async Task RunAsync(ControlledPartitionMovementNativeHostSettings settings,
        NativeLocalhostClusteringOptions localhost, CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? application = null;
        PartitionStorageNativePreStartDisposeOwners? captured = null;
        Task? originalDisposal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            application = await ServerConfiguration.BuildAsync(settings.Arguments(FirstOwner),
                localhostClustering: localhost, startupCancellation: token).ConfigureAwait(false);
            captured = new(application.Services.GetRequiredService<PartitionHostApplicationOwner>(),
                application.Services.GetRequiredService<OrleansNode>(),
                application.Services.GetRequiredService<ServerConnectionRegistry>(),
                application.Services.GetRequiredService<ReplicaTransportLifecycle>());
            await PartitionStorageNativePreStartDisposeAssertions.BeforeAsync(application, captured, settings)
                .ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (application is not null)
        {
            await ServerFailureObserver.ObserveAsync(() =>
            {
                var original = application.DisposeAsync().AsTask();
                originalDisposal = original;
                return original;
            }, failures).ConfigureAwait(false);
        }
        if (captured is not null && originalDisposal is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativePreStartDisposeAssertions.AfterAsync(
                originalDisposal, captured, settings), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
