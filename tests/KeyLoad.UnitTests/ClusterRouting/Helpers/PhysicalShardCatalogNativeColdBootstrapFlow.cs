using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogNativeColdBootstrapFlow
{
    private const int FirstColdCut = 0;
    private const int ColdCuts = 2;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var timing = new NativeRuntimeTestOptions();
            var observed = new bool[ControlledPartitionMovementNativeHostEndpoints.OwnerCount];
            using var deadline = new CancellationTokenSource(timing.StartupTimeout, TimeProvider.System);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            await using var owners = await ControlledPartitionMovementNativeHostFactory.CreateAsync(
                null, null, null, null, null, null, null,
                (index, node) => RequireUnseededRoot(observed, index, node), startup.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(observed.All(value => value)).IsTrue();
                var original = await PhysicalShardCatalogNativeColdBootstrapAssertions.VerifyAsync(owners, token);
                var reply = await ControlledPartitionMovementNativeSeed.ExecuteAsync(owners.Source, token);
                var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(owners.Source,
                    ControlledPartitionMovementCorpus.SeedId, token);
                var operation = entry.Operation ?? throw new InvalidOperationException("The signed seed operation is absent.");
                var receipt = await ControlledPartitionMovementNativeSeed.ReceiptAsync(owners.Source, reply, entry);
                await ControlledPartitionMovementNativeModels.ReadAsync(owners.Source, operation.EvaluatedAt, token);
                CommitReceipt? healthy = null;
                for (var cold = FirstColdCut; cold < ColdCuts; cold++)
                {
                    using var restartDeadline = new CancellationTokenSource(timing.RestartCompletionTimeout, TimeProvider.System);
                    using var restart = CancellationTokenSource.CreateLinkedTokenSource(token, restartDeadline.Token);
                    await owners.RestartAsync(restart.Token);
                    var current = await PhysicalShardCatalogNativeColdBootstrapAssertions.VerifyAsync(owners, token);
                    await PhysicalShardCatalogNativeColdBootstrapAssertions.SameAuthorityAsync(original, current);
                    var replay = await owners.Source.SubmitAsync(OperationKind.Batch, ControlledPartitionMovementCorpus.SeedId,
                        PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(ControlledPartitionMovementCorpus.Seed()), token);
                    await ControlledPartitionMovementNativeSeed.ReceiptAsync(replay, receipt);
                    await ControlledPartitionMovementNativeModels.ReadAsync(owners.Source, operation.EvaluatedAt, token);
                    if (cold == FirstColdCut)
                    {
                        healthy = await ControlledPartitionMovementNativeHealthy.SubmitAsync(owners.Source, token);
                    }
                    else
                    {
                        await ControlledPartitionMovementNativeHealthy.ReplayAsync(owners.Source,
                            healthy ?? throw new InvalidOperationException("The acknowledged continuation receipt is absent."), token);
                    }
                }
            }, failures);
            if (failures.Count != 0)
            {
                owners.RetainRoots();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void RequireUnseededRoot(bool[] observed, int index, ControlledPartitionMovementNativeHost node)
    {
        var path = node.Runtime.Node.Value.DataDirectory;
        if (observed[index] || Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException("The actual Server cold-start root must contain no preseeded files.");
        }
        observed[index] = true;
    }
}
