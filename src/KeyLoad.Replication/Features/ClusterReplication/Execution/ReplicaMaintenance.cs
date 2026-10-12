using Microsoft.Extensions.Logging;

namespace KeyLoad.Replication;

internal sealed class ReplicaMaintenance(ReplicaState state, ReplicaElection election, ReplicaLeader leader, ILogger? logger)
{
    private const int BeforeFirstLogPosition = 0;

    private const string FailedMessage = "Node-owned replica maintenance failed; readiness is fenced.";
    private const int FailedEventId = 1;
    private static readonly Action<ILogger, Exception?> LogFailure = LoggerMessage.Define(LogLevel.Error,
        new EventId(FailedEventId), FailedMessage);
    private Task<ReplicaSnapshot?>? checkpoint;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
                await state.LockedAsync(() =>
                {
                    ScheduleCheckpoint(cancellationToken);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
                await Task.Delay(state.Configuration.HeartbeatInterval, state.Clock, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is KeyLoadException or IOException or System.Text.Json.JsonException)
        {
            state.Poison(error);
            state.Ready = false;
            if (logger is not null)
            {
                LogFailure(logger, null);
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var role = await state.LockedAsync(() => state.Role, cancellationToken).ConfigureAwait(false);
            if (role == ReplicaRole.Leader)
            {
                await leader.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await election.CampaignAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (ReplicaRpcClient.Unavailable(error) && !cancellationToken.IsCancellationRequested) { }
    }

    internal ReplicaNodeState ReadState()
    {
        RequireCheckpoint();
        return state.Snapshot() with
        {
            HasCheckpointPrefix = state.Log.HasCheckpointPrefix,
            CheckpointPending = checkpoint is { IsCompleted: false }
        };
    }

    private void RequireCheckpoint()
    {
        if (checkpoint is { IsFaulted: true })
        {
            state.Poison(checkpoint.Exception!.GetBaseException());
            throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.InvalidSnapshot);
        }
    }

    private void ScheduleCheckpoint(CancellationToken cancellationToken)
    {
        RequireCheckpoint();
        if (checkpoint is { IsCompleted: false })
        {
            return;
        }
        var cut = state.Materializer.Database.LastApplied;
        if (cut - (state.Materializer.Snapshots.Current?.Index ?? BeforeFirstLogPosition) >= state.Configuration.SnapshotThreshold
            || state.Materializer.Log.HasCheckpointPrefix)
        {
            checkpoint = state.Materializer.CreateCheckpointAsync(cancellationToken);
        }
    }

    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (checkpoint is not null)
        {
            try
            { await checkpoint.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (checkpoint.IsCanceled) { }
        }
    }
}
