using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeSnapshots
{
    internal static async Task<ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot>> CaptureAsync(
        List<ControlledPartitionMovementNativeHost> nodes, CancellationToken cancellationToken)
    {
        var snapshots = ImmutableArray.CreateBuilder<ControlledPartitionMovementNativeProcessSnapshot>();
        foreach (var node in nodes)
        {
            snapshots.Add(await ControlledPartitionMovementNativeProcessCapture.ReadAsync(node, cancellationToken).ConfigureAwait(false));
        }
        return snapshots.ToImmutable();
    }
}
