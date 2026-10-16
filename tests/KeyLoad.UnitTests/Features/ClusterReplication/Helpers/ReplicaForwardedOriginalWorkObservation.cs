using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardedOriginalWorkObservation : IGrainRequestPhaseObserver, IGrainRequestNativePhaseObserver, IReplicaForwardedCommandObservation
{
    private readonly Lock gate = new();
    private readonly Dictionary<IReplicaForwardedCommandWork, ReplicaForwardedOriginalWorkObserved> records =
        new(ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource<ReplicaForwardedOriginalWorkObserved> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource execute = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Guid HeldCommand
    {
        get; set;
    }
    internal Task<ReplicaForwardedOriginalWorkObserved> Entered => entered.Task;
    internal Task Cancelled => cancelled.Task;

    public void Created(IReplicaForwardedCommandWork work)
    {
        lock (gate)
        {
            if (records.Count >= ReplicaForwardedOriginalWorkProtocol.MaximumObserved)
            {
                throw new InvalidOperationException(ReplicaForwardedOriginalWorkProtocol.ObservationLimit);
            }
            records.Add(work, new(work)
            {
                Sequence = records.Count
            });
        }
    }

    public async ValueTask BeforeExecuteAsync(ReplicatedOperation operation, IReplicaForwardedCommandWork work,
        CancellationToken cancellationToken)
    {
        var observed = Observe(operation, work);
        if (operation.Id != HeldCommand)
        {
            return;
        }
        entered.TrySetResult(observed);
        try
        {
            await execute.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled.TrySetResult();
            await cleanup.Task.ConfigureAwait(false);
            throw;
        }
    }

    private ReplicaForwardedOriginalWorkObserved Observe(ReplicatedOperation operation, IReplicaForwardedCommandWork work)
    {
        lock (gate)
        {
            var observed = records[work];
            observed.CommandId = operation.Id;
            observed.BeforeExecuteReadBytes = work.Budget.ReadBytes;
            observed.SameBudget = ReferenceEquals(observed.Budget, work.Budget);
            observed.SameBinding = ReferenceEquals(observed.Binding, work.Binding);
            observed.SameLedger = ReferenceEquals(observed.Ledger, work.Binding.ResultRetention);
            return observed;
        }
    }

    internal ReplicaForwardedOriginalWorkObserved ForCommand(Guid commandId)
    {
        lock (gate)
        {
            return records.Values.Where(value => value.CommandId == commandId).OrderBy(value => value.Sequence).LastOrDefault()
                ?? throw new InvalidOperationException(ReplicaForwardedOriginalWorkProtocol.MissingObservation);
        }
    }

    internal void Release()
    {
        execute.TrySetResult();
        cleanup.TrySetResult();
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState actualRuntime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualRuntime);
        cancellationToken.ThrowIfCancellationRequested();
        if (phase == GrainRequestPhase.RequestStarted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return ValueTask.CompletedTask;
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
    }
}
