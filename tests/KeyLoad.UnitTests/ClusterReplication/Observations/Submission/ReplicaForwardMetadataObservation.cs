using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardMetadataProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardMetadataObservation : IGrainRequestPhaseObserver, IGrainRequestNativePhaseObserver, IReplicaForwardedCommandObservation
{
    private readonly Lock gate = new();
    private readonly Dictionary<IReplicaForwardedCommandWork, ReplicaForwardMetadataSample> samples =
        new(ReferenceEqualityComparer.Instance);
    private ReadExecutionBudget? source;
    private Guid commandId;

    internal void Begin(Guid id, ReadExecutionBudget original)
    {
        lock (gate)
        {
            if (source is not null)
            {
                throw new InvalidOperationException(P.Overlap);
            }
            commandId = id;
            source = original;
        }
    }

    internal void End()
    {
        lock (gate)
        {
            source = null;
        }
    }

    public void Created(IReplicaForwardedCommandWork work)
    {
        lock (gate)
        {
            if (source is null)
            {
                return;
            }
            if (samples.Count >= P.MaximumObserved)
            {
                throw new InvalidOperationException(P.Overflow);
            }
            samples.Add(work, new(work, source, commandId, samples.Count, source.ReadBytes, source.ConstrainedNativeRecords));
        }
    }

    public ValueTask BeforeExecuteAsync(ReplicatedOperation operation, IReplicaForwardedCommandWork work,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (samples.TryGetValue(work, out var sample) && operation.Id == sample.ExpectedCommandId)
            {
                sample.ActualCommandId = operation.Id;
                sample.ReceiverExpiry = work.Budget.ExpiresAt;
                sample.ReceiverMaximumReadBytes = work.Budget.MaximumNativeReadBytes;
                sample.ReceiverMaximumRecords = work.Budget.MaximumNativeScanRecords;
            }
        }
        return ValueTask.CompletedTask;
    }

    internal ReplicaForwardMetadataSample For(ReadExecutionBudget original, Guid id)
    {
        lock (gate)
        {
            return samples.Values.Where(sample => ReferenceEquals(sample.Source, original)
                && sample.ActualCommandId == id).OrderBy(sample => sample.Sequence).LastOrDefault() ?? throw new InvalidOperationException(P.Missing);
        }
    }

    internal int CountFor(Guid id)
    {
        lock (gate)
        {
            return samples.Values.Count(sample => sample.ExpectedCommandId == id);
        }
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
