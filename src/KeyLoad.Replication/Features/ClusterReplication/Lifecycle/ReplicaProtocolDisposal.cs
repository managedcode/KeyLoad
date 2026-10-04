namespace KeyLoad.Replication;

internal static class ReplicaProtocolDisposal
{
    internal static async Task DisposeAsync(Task terminalDrain, ReplicaLeader leader, ReplicaSnapshotReceiver snapshots,
        ReplicaFollowerSender followers, CancellationTokenSource lifetime)
    {
        try
        {
            await terminalDrain.ConfigureAwait(false);
        }
        finally
        {
            leader.Dispose();
            snapshots.Dispose();
            followers.Dispose();
            lifetime.Dispose();
        }
    }
}
