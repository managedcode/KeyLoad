using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUncertainProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardUncertainObservation : IGrainRequestPhaseObserver,
    IReplicaForwardedCommandObservation, IReplicaTransportObservation
{
    private readonly Lock gate = new();
    private readonly ReplicaForwardMetadataObservation metadata = new();
    private readonly Dictionary<IReplicaForwardedCommandWork, Guid> receivers = new(ReferenceEqualityComparer.Instance);
    private Guid selected;
    private bool armed;
    private ReadExecutionBudget? source;
    internal long SourceBytesBeforeExecute
    {
        get; private set;
    }
    internal int SourceRecordsBeforeExecute
    {
        get; private set;
    }
    internal Exception Fault
    {
        get;
    }
    internal int CompletedForOriginal
    {
        get; private set;
    }
    internal IReplicaForwardedCommandWork? Receiver
    {
        get; private set;
    }
    internal byte[]? OriginalBytes
    {
        get; private set;
    }
    internal byte[]? ReplyBytes
    {
        get; private set;
    }

    internal ReplicaForwardUncertainObservation(bool timeout)
    {
        Fault = timeout ? new TimeoutException(P.Sentinel) : new SiloUnavailableException(P.Sentinel);
    }

    internal void Arm(Guid commandId, ReadExecutionBudget original)
    {
        metadata.Begin(commandId, original);
        lock (gate)
        {
            selected = commandId;
            source = original;
            armed = true;
        }
    }

    public void Created(IReplicaForwardedCommandWork work)
    {
        metadata.Created(work);
        lock (gate)
        {
            if (receivers.Count >= P.MaximumObserved)
            {
                throw new InvalidOperationException(P.Missing);
            }
            receivers.Add(work, Guid.Empty);
        }
    }

    public ValueTask BeforeExecuteAsync(ReplicatedOperation operation, IReplicaForwardedCommandWork work,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            receivers[work] = operation.Id;
            if (operation.Id == selected && armed)
            {
                Receiver = work;
                SourceBytesBeforeExecute = source!.ReadBytes;
                SourceRecordsBeforeExecute = source.ConstrainedNativeRecords;
            }
        }
        return metadata.BeforeExecuteAsync(operation, work, cancellationToken);
    }

    internal void Begin(Guid id, ReadExecutionBudget original) => metadata.Begin(id, original);
    internal void End() => metadata.End();
    internal ReplicaForwardMetadataSample Sample(ReadExecutionBudget original, Guid id) => metadata.For(original, id);

    public Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>? BeginIncoming(ReplicaRpc method)
        => method == ReplicaRpc.Forward ? Complete : null;

    private void Complete(ReadOnlyMemory<byte> requestBytes, ReadOnlyMemory<byte> replyBytes)
    {
        var request = ReplicaProtocolCodec.Deserialize<ReplicaForwardRequestV1>(requestBytes.Span);
        ReplicaForwardFrameValidation.RequireRequest(request);
        var operation = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(request.Operation.Span);
        lock (gate)
        {
            if (operation.Id != selected)
            {
                return;
            }
            CompletedForOriginal++;
            if (!armed)
            {
                return;
            }
            armed = false;
            OriginalBytes = request.Operation.ToArray();
            ReplyBytes = replyBytes.ToArray();
            throw Fault;
        }
    }

    internal int ReceiversFor(Guid commandId)
    {
        lock (gate)
        {
            return receivers.Values.Count(value => value == commandId);
        }
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
    }
}
