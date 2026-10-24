using KeyLoad.RecoveryTests.StorageRecovery;
using KeyLoad.Server;
using Orleans.TestingHost;
using P = KeyLoad.UnitTests.StorageRecovery.PartitionStorageProducerDisposeProtocol;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartitionStorageProducerDisposeFlow
{
    private const string DirectoryPrefix = "keyload-execution-workers-";
    private const string GuidFormat = "N";

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        var directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
            DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        using var ports = new TestClusterPortAllocator();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var port = ports.AllocateConsecutivePortPairs(1).Item1;
            var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                null, null, false, false, null);
            var arguments = profile.Arguments(directory, port);
            var active = await PartitionStorageProducerDisposeTrial.RunAsync(arguments, profile, token)
                .ConfigureAwait(false);
            var original = await PartitionStorageFlow.RunOwnerAsync(arguments, profile,
                active.Identity, null, token).ConfigureAwait(false);
            for (var cold = P.FirstCold; cold < P.ColdCount; cold++)
            {
                _ = await PartitionStorageFlow.RunOwnerAsync(arguments, profile,
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
