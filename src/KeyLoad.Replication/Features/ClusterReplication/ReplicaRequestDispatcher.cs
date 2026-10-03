namespace KeyLoad.Replication;

internal sealed class ReplicaRequestDispatcher(ReplicaElection election, ReplicaAppendReceiver appends,
    ReplicaLeader leader, ReplicaSnapshotReceiver snapshots, ReplicaConfiguration configuration)
{
    private Func<ReplicatedOperation, CancellationToken, Task<OperationResult>>? forwarded;

    internal void ConfigureForwarding(Func<ReplicatedOperation, CancellationToken, Task<OperationResult>> accept)
    {
        if (Interlocked.CompareExchange(ref forwarded, accept, null) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidPeer);
        }
    }

    internal async Task<ReadOnlyMemory<byte>> HandleAsync(ReplicaRpc method, ReadOnlyMemory<byte> payload, CancellationToken token)
        => method switch
        {
            ReplicaRpc.RequestVote => ReplicaProtocolCodec.Serialize(await election.ReceiveAsync(ReplicaProtocolCodec.Deserialize<VoteRequest>(payload.Span), token).ConfigureAwait(false)),
            ReplicaRpc.Append => ReplicaProtocolCodec.Serialize(await appends.ReceiveAsync(ReplicaProtocolCodec.Deserialize<AppendRequest>(payload.Span), token).ConfigureAwait(false)),
            ReplicaRpc.Forward => ReplicaProtocolCodec.Serialize(await ForwardAsync(ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(payload.Span), token).ConfigureAwait(false)),
            ReplicaRpc.ReadBarrier => await ReadAsync(payload, ReplicaReadRoundPurpose.Application, token).ConfigureAwait(false),
            ReplicaRpc.ControlReadBarrier => await ReadAsync(payload, ReplicaReadRoundPurpose.Control, token).ConfigureAwait(false),
            ReplicaRpc.ReadProbe => ReplicaProtocolCodec.Serialize(await appends.ReceiveAsync(ReplicaReadRoundGuard.Probe(payload, configuration), token).ConfigureAwait(false)),
            ReplicaRpc.SnapshotBegin => ReplicaProtocolCodec.Serialize(await snapshots.BeginAsync(ReplicaProtocolCodec.Deserialize<SnapshotBeginRequest>(payload.Span), token).ConfigureAwait(false)),
            ReplicaRpc.SnapshotChunk => ReplicaProtocolCodec.Serialize(await snapshots.ChunkAsync(ReplicaProtocolCodec.Deserialize<SnapshotChunkRequest>(payload.Span), token).ConfigureAwait(false)),
            ReplicaRpc.SnapshotComplete => ReplicaProtocolCodec.Serialize(await snapshots.CompleteAsync(ReplicaProtocolCodec.Deserialize<SnapshotCompleteRequest>(payload.Span), token).ConfigureAwait(false)),
            _ => throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer)
        };

    private async Task<ReadOnlyMemory<byte>> ReadAsync(ReadOnlyMemory<byte> payload, ReplicaReadRoundPurpose purpose, CancellationToken token)
    {
        ReplicaReadRoundGuard.Empty(payload);
        return ReplicaProtocolCodec.Serialize(await leader.BarrierAsync(purpose, token).ConfigureAwait(false));
    }

    private Task<OperationResult> ForwardAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
        => (Volatile.Read(ref forwarded) ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader))(operation, cancellationToken);
}
