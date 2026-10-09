using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class ConnectionRf3OperationJoin
{
    private readonly List<(Guid Arm, Task Original)> operations = [];
    private readonly HashSet<Task> terminalObserved = [];

    internal bool HasPending => operations.Any(static operation => !operation.Original.IsCompleted);

    internal void Retain(Guid arm, Task original) => operations.Add((arm, original));

    internal bool ProducersDisposed(RequestCqrsProbeFixture controls)
        => operations.All(operation =>
        {
            var arm = controls.ArmFor(operation.Arm);
            return arm.RequestId is null || arm.ProducerDisposedSeen && arm.Settled;
        });

    internal async Task JoinBoundedAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            await ServerFailureObserver.ObserveAsync(
                () => operation.Original.WaitAsync(cancellationToken), failures).ConfigureAwait(false);
            if (operation.Original.IsCompleted) { terminalObserved.Add(operation.Original); }
        }
    }

    internal async Task JoinAfterTransportDisposedAsync(List<Exception> failures)
    {
        foreach (var operation in operations)
        {
            if (terminalObserved.Contains(operation.Original))
            {
                await operation.Original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                continue;
            }
            await ServerFailureObserver.ObserveAsync(() => operation.Original, failures).ConfigureAwait(false);
            terminalObserved.Add(operation.Original);
        }
    }

    internal async Task JoinProducersAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            var arm = controls.ArmFor(operation.Arm);
            if (arm.RequestId is not { } requestId || arm.ProducerDisposedSeen && arm.Settled) { continue; }
            await ServerFailureObserver.ObserveAsync(
                () => RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, arm.ArmId,
                    requestId, arm.CommandId, discovery, cancellationToken), failures).ConfigureAwait(false);
        }
    }
}
