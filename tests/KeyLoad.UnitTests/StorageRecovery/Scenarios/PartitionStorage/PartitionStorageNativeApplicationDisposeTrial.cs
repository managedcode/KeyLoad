using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeApplicationDisposeTrial
{
    private const int NoFailures = 0;

    internal static async Task<PartitionStorageNativePartialStart> RunAsync(
        string[] arguments, NativeLocalhostClusteringOptions localhost,
        RequestCqrsHostedProfile profile, CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? application = null;
        PartitionStorageNativePartialStart? observation = null;
        PartitionStorageNativeApplicationDisposeCapture? captured = null;
        Dictionary<string, byte[]>? files = null;
        long maximumBytes = NoFailures;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(profile.Timing.StartupTimeout, TimeProvider.System);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
            application = await ServerConfiguration.BuildAsync(arguments,
                localhostClustering: localhost,
                lifecycleParticipantFactory: provider => observation = new(
                    provider.GetRequiredService<PartitionHostApplicationOwner>()),
                configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
            var owner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
            var starting = application.StartAsync(startup.Token);
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
            await Assert.That(captured.Node.Closing is null).IsTrue();
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
