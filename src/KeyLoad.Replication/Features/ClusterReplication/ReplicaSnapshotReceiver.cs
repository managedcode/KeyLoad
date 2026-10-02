namespace KeyLoad.Replication;

internal sealed class ReplicaSnapshotReceiver(ReplicaState state) : IDisposable
{
    private readonly SemaphoreSlim transferGate = new(1, 1);

    /// <inheritdoc />
    public void Dispose() => transferGate.Dispose();

    internal Task<SnapshotReply> BeginAsync(SnapshotBeginRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(request.Term, request.LeaderId, async () =>
        {
            var offset = await state.Materializer.BeginCheckpointAsync(request.Snapshot, cancellationToken).ConfigureAwait(false);
            var published = state.Materializer.Snapshots.Current;
            return (offset, published?.TransferId == request.Snapshot.TransferId, published?.Index ?? 0);
        }, cancellationToken);

    internal Task<SnapshotReply> ChunkAsync(SnapshotChunkRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(request.Term, request.LeaderId, async () =>
        {
            var offset = await Task.Run(() => state.Materializer.Snapshots.Append(request.TransferId, request.Offset, request.Bytes.Span),
                cancellationToken).ConfigureAwait(false);
            return (offset, false, 0L);
        }, cancellationToken);

    internal Task<SnapshotReply> CompleteAsync(SnapshotCompleteRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(request.Term, request.LeaderId, async () =>
        {
            var snapshot = await state.Materializer.InstallCheckpointAsync(request.TransferId, cancellationToken).ConfigureAwait(false);
            return (snapshot.Length, true, snapshot.Index);
        }, cancellationToken);

    private async Task<SnapshotReply> ExecuteAsync(long term, string leader,
        Func<Task<(long Offset, bool Installed, long Index)>> apply, CancellationToken cancellationToken)
    {
        await transferGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accepted = await state.LockedAsync(() => state.ObserveLeader(term, leader), cancellationToken).ConfigureAwait(false);
            if (!accepted)
            {
                return new(state.Log.State.Term, 0, false, 0);
            }
            var result = await apply().ConfigureAwait(false);
            return new(state.Log.State.Term, result.Offset, result.Installed, result.Index);
        }
        finally { transferGate.Release(); }
    }

    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        await transferGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        transferGate.Release();
    }
}
