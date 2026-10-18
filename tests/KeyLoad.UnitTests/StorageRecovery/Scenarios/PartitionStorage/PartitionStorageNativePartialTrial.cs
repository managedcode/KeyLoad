using KeyLoad.RecoveryTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePartialTrial
{
    private const int FirstOwner = 0;
    private const int NoFailures = 0;

    internal static async Task<PartitionStorageNativePartialStart> RunAsync(
        ControlledPartitionMovementNativeHostSettings settings, NativeLocalhostClusteringOptions localhost,
        ControlledPartitionMovementNativeHostEndpoints endpoints, CancellationToken token)
    {
        var failures = new List<Exception>();
        ControlledPartitionMovementNativeHost? host = null;
        PartitionHostApplicationOwner? owner = null;
        PartitionStorageNativePartialStart? observation = null;
        PartitionStorageLifecycle? lower = null;
        var refusalProved = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            host = await ControlledPartitionMovementNativeHost.CreateAsync(settings.Arguments(FirstOwner), localhost,
                lifecycleParticipantFactory: provider => observation = new(
                    provider.GetRequiredService<PartitionHostApplicationOwner>()),
                startupCancellation: token).ConfigureAwait(false);
            owner = host.Application.Services.GetRequiredService<PartitionHostApplicationOwner>();
            lower = host.Application.Services.GetRequiredService<PartitionStorageLifecycle>();
            endpoints.Dispose();
            var originalStart = host.StartAsync(token);
            var expected = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => originalStart, expected).ConfigureAwait(false);
            var proof = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativePartialAssertions.RequireAsync(
                observation!, owner, originalStart, expected), proof).ConfigureAwait(false);
            if (proof.Count != NoFailures)
            {
                expected.AddRange(proof);
                ServerFailureObserver.ThrowIfAny(expected);
            }
            refusalProved = true;
        }, failures).ConfigureAwait(false);
        if (host is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativePartialClose.RunAsync(
                host, owner, lower, refusalProved, failures), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return observation!;
    }
}
