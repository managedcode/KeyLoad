using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2InventoryRecoveryOwners
{
    internal static async Task WithAsync(string root, Func<ControlledPartitionMovementNativeNode,
        ControlledPartitionMovementNativeNode, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNativeNode(
                Path.Combine(root, D2InventoryCrashProtocol.SourceDirectory), D2InventoryCrashCorpus.Source.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var target = new ControlledPartitionMovementNativeNode(
                    Path.Combine(root, D2InventoryCrashProtocol.TargetDirectory), D2InventoryCrashCorpus.Target.Owner);
                await ServerFailureObserver.ObserveAsync(() => operation(source, target), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
