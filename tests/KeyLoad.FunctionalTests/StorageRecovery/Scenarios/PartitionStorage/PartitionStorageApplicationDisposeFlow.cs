using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Server;
using Orleans.TestingHost;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionStorageApplicationDisposeFlow
{
    private const string DirectoryPrefix = "keyload-execution-dispose-";
    private const string GuidFormat = "N";
    private const string RetainedRoot = "KeyLoad.RetainedRoot";
    private const int OneSilo = 1;
    private const int FirstCold = 0;
    private const int ColdCount = 2;
    private const int NoFailures = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        var directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
            DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        using var ports = new TestClusterPortAllocator();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var port = ports.AllocateConsecutivePortPairs(OneSilo).Item1;
            var profile = new RequestCqrsHostedProfile(TimeProvider.System, UnitRoutingOptions.Routing(),
                null, null, false, false, null);
            var arguments = profile.Arguments(directory, port);
            var partial = await PartitionStorageApplicationDisposeTrial.RunAsync(arguments, profile, token)
                .ConfigureAwait(false);
            var original = await PartitionStorageFlow.RunOwnerAsync(arguments, profile,
                partial.Identity!, null, token)
                .ConfigureAwait(false);
            for (var cold = FirstCold; cold < ColdCount; cold++)
            {
                _ = await PartitionStorageFlow.RunOwnerAsync(arguments, profile,
                    partial.Identity!, original, token).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(ports.Dispose, failures);
        }
        if (failures.Count == NoFailures && Directory.Exists(directory))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
        }
        if (failures.Count != NoFailures)
        {
            ServerFailureObserver.Observe(() => failures[NoFailures].Data[RetainedRoot] = directory, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
