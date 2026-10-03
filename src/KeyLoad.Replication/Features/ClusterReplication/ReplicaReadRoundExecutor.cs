namespace KeyLoad.Replication;

internal sealed class ReplicaReadRoundExecutor(ReplicaState state, ReplicaRpcClient rpc, ReplicaLeader leader,
    ReplicaActivityTracker activity, Task transportReady, CancellationToken lifetime)
{
    internal async Task ExecuteAsync(ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        request.CancelAfter(ReplicaProtocol.ReadBarrierTimeout);
        await transportReady.WaitAsync(request.Token).ConfigureAwait(false);
        var route = await state.LockedAsync(() => (state.Role, state.LeaderId), request.Token).ConfigureAwait(false);
        if (route.Role == ReplicaRole.Leader)
        {
            await leader.BarrierAsync(purpose, request.Token).ConfigureAwait(false);
            return;
        }
        if (route.LeaderId is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
        var method = purpose == ReplicaReadRoundPurpose.Control ? ReplicaRpc.ControlReadBarrier : ReplicaRpc.ReadBarrier;
        var barrier = await rpc.InvokeAsync<string, ReadBarrierReceipt>(route.LeaderId, method,
            string.Empty, request.Token).ConfigureAwait(false);
        if (barrier.Incarnation != state.Configuration.Incarnation || barrier.Position < 1 || barrier.Term < 1)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
        }
        await state.Materializer.WaitForApplyAsync(barrier.Position, request.Token).ConfigureAwait(false);
    }
}
