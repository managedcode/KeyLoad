using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost
{
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        { return new(shutdown ??= StopCoreAsync()); }
    }

    internal Task StopAsync(CancellationToken cancellationToken)
        => DisposeAsync().AsTask().WaitAsync(cancellationToken);

    private async Task StopCoreAsync()
    {
        using var deadline = new CancellationTokenSource(Runtime.ServerExecution.Value.ShutdownTimeout, Clock);
        var token = deadline.Token;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(application.Lifetime.StopApplication, failures);
        Task? nodeStop = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            nodeStop = Silo.StopAsync(token);
            await nodeStop.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (nodeStop is not { IsCompletedSuccessfully: true } || !Silo.HasJoinedRequestWork)
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable));
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(() => Silo.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        Task? httpStop = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            httpStop = application.StopAsync(token);
            await httpStop.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            applicationDisposal ??= application.DisposeAsync().AsTask();
            await applicationDisposal.WaitAsync(token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (httpStop is not { IsCompletedSuccessfully: true }
            && applicationDisposal is not { IsCompletedSuccessfully: true })
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable));
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            partitionDisposal ??= Partition.DisposeAsync().AsTask();
            await partitionDisposal.WaitAsync(token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (partitionDisposal is { IsCompletedSuccessfully: true })
        { Volatile.Write(ref joined, true); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
