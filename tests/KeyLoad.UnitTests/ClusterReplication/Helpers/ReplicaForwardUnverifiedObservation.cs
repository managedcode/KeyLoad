using System.Runtime.InteropServices;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUnverifiedProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Changes only a genuine selected post-MAC payload; never creates a reply, outcome or provider.</summary>
internal sealed class ReplicaForwardUnverifiedObservation : IGrainRequestPhaseObserver,
    IGrainRequestNativePhaseObserver, IReplicaForwardedCommandObservation, IReplicaTransportObservation
{
    private readonly ReplicaForwardMetadataObservation actual = new();
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, int> counts = [];
    private Guid selected;
    private bool corrupt;

    internal void Begin(Guid id, ReadExecutionBudget work, bool corruptReply)
    {
        lock (gate)
        {
            selected = id;
            corrupt = corruptReply;
        }
        actual.Begin(id, work);
    }

    internal void End()
    {
        actual.End();
        lock (gate)
        {
            selected = Guid.Empty;
            corrupt = false;
        }
    }

    internal int Count(Guid id)
    {
        lock (gate)
        {
            return counts.GetValueOrDefault(id);
        }
    }

    internal ReplicaForwardMetadataSample Sample(ReadExecutionBudget work, Guid id) => actual.For(work, id);

    public Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>? BeginIncoming(ReplicaRpc method)
        => method == ReplicaRpc.Forward ? ObserveReply : null;

    private void ObserveReply(ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> reply)
    {
        Guid id;
        lock (gate)
        {
            id = selected;
        }
        if (id == Guid.Empty)
        {
            return;
        }
        var frame = ReplicaProtocolCodec.Deserialize<ReplicaForwardRequestV1>(request.Span);
        var operation = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(frame.Operation.Span);
        if (operation.Id != id)
        {
            return;
        }
        bool change;
        lock (gate)
        {
            if (counts.Count >= P.MaximumObserved && !counts.ContainsKey(id))
            {
                throw new InvalidOperationException(P.BoundExceeded);
            }
            counts[id] = checked(counts.GetValueOrDefault(id) + P.One);
            change = corrupt;
            corrupt = false;
        }
        if (!change)
        {
            return;
        }
        if (reply.IsEmpty || !MemoryMarshal.TryGetArray(reply, out var owned))
        {
            throw new InvalidOperationException(P.OwnedReplyRequired);
        }
        // The native publisher already signed this exact array. Keep that genuine original signature unchanged.
        owned.AsSpan()[P.FirstByte] ^= P.ChangedBit;
    }

    public void Created(IReplicaForwardedCommandWork work) => actual.Created(work);
    public ValueTask BeforeExecuteAsync(ReplicatedOperation operation, IReplicaForwardedCommandWork work,
        CancellationToken token) => actual.BeforeExecuteAsync(operation, work, token);
    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken token) => actual.ObserveAsync(identity, phase, context, token);
    public ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState runtime, CancellationToken token) => actual.ObserveNativeAsync(identity, phase, runtime, token);
    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context) => actual.ProducerDisposed(identity, context);
}
