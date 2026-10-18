using KeyLoad.RecoveryTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeApplicationDisposeTrial
{
    private const int FirstOwner = 0;
    private const int NoFailures = 0;

    internal static async Task<PartitionStorageNativePartialStart> RunAsync(
        ControlledPartitionMovementNativeHostSettings settings, NativeLocalhostClusteringOptions localhost,
        ControlledPartitionMovementNativeHostEndpoints endpoints, CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? application = null;
        PartitionStorageNativePartialStart? observation = null;
        PartitionStorageNativeApplicationDisposeCapture? captured = null;
        Dictionary<string, byte[]>? files = null;
        long maximumBytes = NoFailures;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            application = await ServerConfiguration.BuildAsync(settings.Arguments(FirstOwner),
                localhostClustering: localhost,
                lifecycleParticipantFactory: provider => observation = new(
                    provider.GetRequiredService<PartitionHostApplicationOwner>()),
                startupCancellation: token).ConfigureAwait(false);
            var owner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
            endpoints.Dispose();
            var starting = application.StartAsync(token);
            var expected = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => starting, expected).ConfigureAwait(false);
            var proof = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativePartialAssertions.RequireAsync(
                observation!, owner, starting, expected), proof).ConfigureAwait(false);
            if (proof.Count != NoFailures)
            {
                expected.AddRange(proof);
                ServerFailureObserver.ThrowIfAny(expected);
            }
            captured = PartitionStorageNativeApplicationDisposeCapture.Read(application.Services);
            await Assert.That(owner.Closing is null).IsTrue();
            await Assert.That(owner.StorageProducerJoins is null).IsTrue();
            maximumBytes = owner.Borrow().Database.Limits.MaxQueryReadBytes;
            files = await PartitionStorageNativeApplicationDisposeFiles.ReadAsync(captured.Directory,
                maximumBytes, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await PartitionStorageNativeApplicationDisposeSettlement.CloseAsync(application, captured, files,
            maximumBytes, failures, token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return observation!;
    }
}
