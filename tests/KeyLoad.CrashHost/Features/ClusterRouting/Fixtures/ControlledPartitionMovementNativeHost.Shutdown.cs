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
            nodeStop = Silo.StopAsync(CancellationToken.None);
            await JoinObservedAsync(nodeStop, failures, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        var nativeHostReleased = false;
        if (nodeStop is { IsCompleted: true } && Silo.HasJoinedRequestWork)
        { ServerFailureObserver.Observe(() => nativeHostReleased = Silo.Discovery is null, failures); }
        if (nodeStop is not { IsCompleted: true } || !Silo.HasJoinedRequestWork || !nativeHostReleased)
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
            await JoinObservedAsync(applicationDisposal, failures, token).ConfigureAwait(false);
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
            await JoinObservedAsync(partitionDisposal, failures, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (partitionDisposal is { IsCompletedSuccessfully: true })
        {
            ServerFailureObserver.Observe(NativeObservation.Dispose, failures);
            Volatile.Write(ref joined, true);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task JoinObservedAsync(Task original, List<Exception> failures,
        CancellationToken token)
    {
        var observation = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(token), observation).ConfigureAwait(false);
        failures.AddRange(observation);
        if (observation.Count == NoCommandFailures)
        { return; }
        var completion = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, completion).ConfigureAwait(false);
        foreach (var error in completion)
        {
            if (!observation.Any(observed => ReferenceEquals(observed, error)))
            { failures.Add(error); }
        }
    }

}
