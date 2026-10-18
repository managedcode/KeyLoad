using System.Net;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using Orleans.TestingHost;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProducerDisposeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeProducerDisposeFlow
{
    private const string DirectoryPrefix = "keyload-native-workers-";
    private const string GuidFormat = "N";
    private const int OneSilo = 1;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        var directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(Path.GetTempPath())),
            DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        using var ports = new TestClusterPortAllocator();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var port = ports.AllocateConsecutivePortPairs(OneSilo).Item1;
            var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                null, null, false, false, null);
            var arguments = profile.Arguments(directory, port);
            var localhost = new NativeLocalhostClusteringOptions(new IPEndPoint(IPAddress.Loopback, port));
            var active = await PartitionStorageNativeProducerDisposeTrial.RunAsync(arguments, localhost, profile, token)
                .ConfigureAwait(false);
            var original = await PartitionStorageNativeFlow.RunOwnerAsync(arguments, localhost, profile,
                active.Identity, null, token).ConfigureAwait(false);
            for (var cold = P.FirstCold; cold < P.ColdCount; cold++)
            {
                _ = await PartitionStorageNativeFlow.RunOwnerAsync(arguments, localhost, profile,
                    active.Identity, original, token).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (failures.Count == P.NoFailures)
        {
            ServerFailureObserver.Observe(ports.Dispose, failures);
        }
        if (failures.Count == P.NoFailures && Directory.Exists(directory))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
