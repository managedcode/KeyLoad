using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessChild
{
    internal static async Task RunAsync(string root, string mode,
        ControlledPartitionMovementNativeProcessInput input, CancellationToken cancellationToken)
    {
        if (mode is not (ControlledPartitionMovementNativeProcessProtocol.Hold
            or ControlledPartitionMovementNativeProcessProtocol.Observe))
        {
            throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        }
        ControlledPartitionMovementNativeHosts? owners = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owners = await ControlledPartitionMovementNativeHosts.OpenAsync(input.Hosts, root, cancellationToken);
            if (mode == ControlledPartitionMovementNativeProcessProtocol.Hold)
            {
                await CrashHostPause.WaitForKillAsync();
                return;
            }
            var before = await owners.CaptureAsync(cancellationToken);
            var reply = await ControlledPartitionMovementNativeProcessOutcome.ReadAsync(owners, input.Original, cancellationToken);
            var after = await owners.CaptureAsync(cancellationToken);
            await ControlledPartitionMovementProcessFiles.WriteAsync(root,
                ControlledPartitionMovementNativeProcessProtocol.ResultFile,
                new ControlledPartitionMovementNativeProcessResult(before, after, reply.OriginalBytes, reply.Signature), cancellationToken);
        }, failures);
        if (owners is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => owners.CloseRetainingRootAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
