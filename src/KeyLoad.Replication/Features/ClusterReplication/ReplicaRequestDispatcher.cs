using System.Text;

namespace KeyLoad.Replication;

internal sealed class ReplicaRequestDispatcher(ReplicaElection election, ReplicaAppendReceiver appends,
    ReplicaLeader leader, ReplicaSnapshotReceiver snapshots)
{
    private Func<ReplicatedOperation, CancellationToken, Task<OperationResult>>? forwarded;

    internal void ConfigureForwarding(Func<ReplicatedOperation, CancellationToken, Task<OperationResult>> accept)
    {
        if (Interlocked.CompareExchange(ref forwarded, accept, null) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidPeer);
        }
    }

    internal async Task<string> HandleAsync(ReplicaRpc method, string payloadJson, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(payloadJson);
        object result = method switch
        {
            ReplicaRpc.RequestVote => await election.ReceiveAsync(ReplicaProtocolCodec.Deserialize<VoteRequest>(bytes), token).ConfigureAwait(false),
            ReplicaRpc.Append => await appends.ReceiveAsync(ReplicaProtocolCodec.Deserialize<AppendRequest>(bytes), token).ConfigureAwait(false),
            ReplicaRpc.Forward => await ForwardAsync(ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(bytes), token).ConfigureAwait(false),
            ReplicaRpc.ReadBarrier => await leader.BarrierAsync(token).ConfigureAwait(false),
            ReplicaRpc.SnapshotBegin => await snapshots.BeginAsync(ReplicaProtocolCodec.Deserialize<SnapshotBeginRequest>(bytes), token).ConfigureAwait(false),
            ReplicaRpc.SnapshotChunk => await snapshots.ChunkAsync(ReplicaProtocolCodec.Deserialize<SnapshotChunkRequest>(bytes), token).ConfigureAwait(false),
            ReplicaRpc.SnapshotComplete => await snapshots.CompleteAsync(ReplicaProtocolCodec.Deserialize<SnapshotCompleteRequest>(bytes), token).ConfigureAwait(false),
            _ => throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer)
        };
        return Encoding.UTF8.GetString(ReplicaProtocolCodec.Serialize(result));
    }

    private Task<OperationResult> ForwardAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
        => (Volatile.Read(ref forwarded) ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader))(operation, cancellationToken);
}
