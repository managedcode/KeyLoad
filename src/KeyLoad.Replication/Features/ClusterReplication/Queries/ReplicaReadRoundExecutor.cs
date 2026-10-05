using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.Replication;

internal sealed class ReplicaReadRoundExecutor(ReplicaState state, ReplicaRpcClient rpc, ReplicaLeader leader,
    ReplicaActivityTracker activity, Task transportReady, CancellationToken lifetime, TimeSpan readBarrierTimeout)
{
    private const int FirstCommittedPosition = 1;
    private const int FirstElectionTerm = 1;

    internal async Task ExecuteAsync(ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        request.CancelAfter(readBarrierTimeout);
        var transportReadyStarted = DatabasePhaseTelemetry.Begin();
        var transportReadyOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            await transportReady.WaitAsync(request.Token).ConfigureAwait(false);
            transportReadyOutcome = DatabasePhaseOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            transportReadyOutcome = DatabasePhaseTelemetry.CancellationOutcome(cancellationToken, request.Token);
            throw;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ReadTransportReady, transportReadyOutcome, transportReadyStarted);
        }

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
        if (barrier.Incarnation != state.Configuration.Incarnation || barrier.Position < FirstCommittedPosition || barrier.Term < FirstElectionTerm)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
        }
        await state.Materializer.WaitForApplyAsync(barrier.Position, request.Token).ConfigureAwait(false);
    }
}
