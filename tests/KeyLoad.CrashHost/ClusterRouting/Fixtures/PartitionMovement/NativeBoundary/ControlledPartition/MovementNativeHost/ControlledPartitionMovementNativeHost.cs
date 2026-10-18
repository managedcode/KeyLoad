using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost
{
    private const int UnknownColdShutdownNode = -1;
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
        var budgetFailures = new List<Exception>();
        var budget = new ControlledNativeColdOwnerBudget(Runtime.ServerExecution.Value.ShutdownTimeout, Clock, budgetFailures);
        using var deadline = new CancellationTokenSource(budget.Timeout, budget.Clock);
        var token = deadline.Token;
        var failures = new List<Exception>();
        var owner = storageOwner();
        if (startup is { } opening)
        {
            await opening.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        ServerFailureObserver.Observe(Application.Lifetime.StopApplication, failures);
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
        var discovery = silo().Discovery;
        if (!await JoinNativeWorkAsync(failures, budget, budgetFailures, token).ConfigureAwait(false))
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => Application.StopAsync(CancellationToken.None), failures,
            Volatile.Read(ref coldShutdownNode), ControlledNativeColdShutdownEvidence.Stage.HttpStop, false,
            token, CancellationToken.None).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            storageStop ??= storageLifecycle.StopAsync(CancellationToken.None);
            await JoinObservedAsync(storageStop, failures, ControlledNativeColdShutdownEvidence.Stage.PartitionDisposalObservation,
                budget, budgetFailures, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (owner.Closing is not { IsCompleted: true } || !owner.CanReleaseRoot)
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable));
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            applicationDisposal ??= Application.DisposeAsync().AsTask();
            await JoinObservedAsync(applicationDisposal, failures, ControlledNativeColdShutdownEvidence.Stage.ApplicationDisposalObservation,
                budget, budgetFailures, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (applicationDisposal is { IsCompletedSuccessfully: true })
        {
            ServerFailureObserver.Observe(() => ControlledPartitionMovementNativePublication.RequireClosed(silo(), discovery), failures);
            Volatile.Write(ref joined, silo().HasJoinedRequestWork && discovery?.Read().TransportReady != true);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task<bool> JoinNativeWorkAsync(List<Exception> failures, ControlledNativeColdOwnerBudget budget,
        List<Exception> budgetFailures, CancellationToken token)
    {
        var native = silo();
        Task? original = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            original = native.StopAsync(CancellationToken.None);
            await JoinObservedAsync(original, failures, ControlledNativeColdShutdownEvidence.Stage.NodeStopObservation,
                budget, budgetFailures, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (original is not { IsCompleted: true } || !native.HasJoinedRequestWork)
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable));
            return false;
        }
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => native.DisposeAsync().AsTask(), failures,
            Volatile.Read(ref coldShutdownNode), ControlledNativeColdShutdownEvidence.Stage.NodeDisposal, false,
            token, CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private Task JoinObservedAsync(Task original, List<Exception> failures,
        ControlledNativeColdShutdownEvidence.Stage stage, ControlledNativeColdOwnerBudget budget,
        List<Exception> budgetFailures, CancellationToken token)
        => ControlledPartitionMovementNativeTaskJoin.JoinBudgetedAsync(original, failures,
            () => Volatile.Read(ref coldShutdownNode), stage, budget, budgetFailures, silo(), token);
}
