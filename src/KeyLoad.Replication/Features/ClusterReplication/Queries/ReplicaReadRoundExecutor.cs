using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.Replication;

internal sealed class ReplicaReadRoundExecutor(ReplicaState state, ReplicaRpcClient rpc, ReplicaLeader leader,
    ReplicaActivityTracker activity, Task transportReady, TimeSpan readBarrierTimeout, CancellationToken lifetime)
{
    private const int FirstCommittedPosition = 1;
    private const int FirstElectionTerm = 1;

    internal async Task ExecuteAsync(ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        using var active = activity.Enter();
        using var deadline = new CancellationTokenSource(readBarrierTimeout, state.Clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime, deadline.Token);
        var diagnosticStage = ReplicaReadFailureMetadata.Transport;
        try
        {
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

            diagnosticStage = ReplicaReadFailureMetadata.Route;
            var route = await state.LockedAsync(() => (state.Role, state.LeaderId), request.Token).ConfigureAwait(false);
            if (route.Role == ReplicaRole.Leader)
            {
                diagnosticStage = ReplicaReadFailureMetadata.LeaderRound;
                await leader.BarrierAsync(purpose, request.Token).ConfigureAwait(false);
                return;
            }
            if (route.LeaderId is null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
            }
            var method = purpose == ReplicaReadRoundPurpose.Control ? ReplicaRpc.ControlReadBarrier : ReplicaRpc.ReadBarrier;
            diagnosticStage = ReplicaReadFailureMetadata.PeerRound;
            var barrier = await rpc.InvokeAsync<string, ReadBarrierReceipt>(route.LeaderId, method,
                string.Empty, request.Token).ConfigureAwait(false);
            if (barrier.Incarnation != state.Configuration.Incarnation || barrier.Position < FirstCommittedPosition || barrier.Term < FirstElectionTerm)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
            }
            diagnosticStage = ReplicaReadFailureMetadata.Apply;
            await state.Materializer.WaitForApplyAsync(barrier.Position, request.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            ReplicaReadFailureMetadata.Mark(error, diagnosticStage, cancellationToken.IsCancellationRequested,
                lifetime.IsCancellationRequested, deadline.IsCancellationRequested);
            throw;
        }
    }
}
