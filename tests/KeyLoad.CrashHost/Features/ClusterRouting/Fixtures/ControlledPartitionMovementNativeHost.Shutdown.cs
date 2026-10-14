using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost
{
    private const int UnknownColdShutdownNode = -1;
    private const int OriginalJoinStageOffset = 1;
    private int coldShutdownNode = UnknownColdShutdownNode;

    internal void BindColdShutdownEvidence(int nodeIndex) => Volatile.Write(ref coldShutdownNode, nodeIndex);

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(shutdown ??= StopCoreAsync());
        }
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
            await JoinObservedAsync(nodeStop, failures, ControlledNativeColdShutdownEvidence.Stage.NodeStopObservation, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        var nativeHostReleased = false;
        if (nodeStop is { IsCompleted: true } && Silo.HasJoinedRequestWork)
        {
            ServerFailureObserver.Observe(() => nativeHostReleased = Silo.Discovery is null, failures);
        }
        if (nodeStop is not { IsCompleted: true } || !Silo.HasJoinedRequestWork || !nativeHostReleased)
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable));
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => Silo.DisposeAsync().AsTask(), failures,
            Volatile.Read(ref coldShutdownNode), ControlledNativeColdShutdownEvidence.Stage.NodeDisposal, false,
            token, CancellationToken.None).ConfigureAwait(false);
        Task? httpStop = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            httpStop = application.StopAsync(token);
            await ControlledNativeColdShutdownEvidence.ObserveAsync(() => httpStop, failures,
                Volatile.Read(ref coldShutdownNode), ControlledNativeColdShutdownEvidence.Stage.HttpStop, false,
                token, CancellationToken.None).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            applicationDisposal ??= application.DisposeAsync().AsTask();
            await JoinObservedAsync(applicationDisposal, failures, ControlledNativeColdShutdownEvidence.Stage.ApplicationDisposalObservation, token).ConfigureAwait(false);
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
            await JoinObservedAsync(partitionDisposal, failures, ControlledNativeColdShutdownEvidence.Stage.PartitionDisposalObservation, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (partitionDisposal is { IsCompletedSuccessfully: true })
        {
            ServerFailureObserver.Observe(NativeObservation.Dispose, failures);
            Volatile.Write(ref joined, true);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task JoinObservedAsync(Task original, List<Exception> failures,
        ControlledNativeColdShutdownEvidence.Stage stage, CancellationToken token)
    {
        var observation = new List<Exception>();
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, observation,
            Volatile.Read(ref coldShutdownNode), stage, true, token, CancellationToken.None).ConfigureAwait(false);
        failures.AddRange(observation);
        if (observation.Count == NoCommandFailures)
        {
            return;
        }
        if (stage == ControlledNativeColdShutdownEvidence.Stage.NodeStopObservation)
        {
            await ServerFailureObserver.ObserveAsync(() =>
                ControlledNativeColdShutdownEvidence.CaptureNodeStopOwnerStateAsync(
                    Volatile.Read(ref coldShutdownNode), original, Silo.HasJoinedRequestWork,
                    Silo.Discovery is not null), failures).ConfigureAwait(false);
        }
        var completion = new List<Exception>();
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, completion,
            Volatile.Read(ref coldShutdownNode), (ControlledNativeColdShutdownEvidence.Stage)((int)stage + OriginalJoinStageOffset), false, token, CancellationToken.None).ConfigureAwait(false);
        foreach (var error in completion)
        {
            if (!observation.Any(observed => ReferenceEquals(observed, error)))
            {
                failures.Add(error);
            }
        }
        await ControlledNativeColdShutdownEvidence.AfterJoinAsync(observation, original, failures,
            Volatile.Read(ref coldShutdownNode), stage).ConfigureAwait(false);
    }

}
