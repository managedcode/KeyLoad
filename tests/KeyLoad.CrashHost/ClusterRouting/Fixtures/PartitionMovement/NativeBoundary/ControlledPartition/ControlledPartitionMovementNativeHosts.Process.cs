using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHosts
{
    private const int FirstRetainedNode = 0;

    internal async Task<ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot>> CaptureAsync(
        CancellationToken cancellationToken)
        => await ControlledPartitionMovementNativeSnapshots.CaptureAsync(nodes, cancellationToken).ConfigureAwait(false);

    internal ControlledPartitionMovementNativeHostInput CaptureInput() => settings.CaptureInput();

    internal static async Task<ControlledPartitionMovementNativeHosts> OpenAsync(
        ControlledPartitionMovementNativeHostInput input, string originalRoot,
        CancellationToken cancellationToken)
        => await ControlledPartitionMovementNativeReopen.OpenAsync(input, originalRoot, FirstRetainedNode,
            cancellationToken).ConfigureAwait(false);
}
