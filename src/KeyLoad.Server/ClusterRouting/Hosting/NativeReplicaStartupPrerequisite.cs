using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class NativeReplicaStartupPrerequisite
{
    private const long BeforeFirstConsensusTerm = 0;
    private const long BeforeFirstCommittedEntry = 0;

    internal static async Task WaitAsync(OrleansNode node, PartitionHost partition, TimeProvider clock,
        PhysicalShardCatalogPrerequisiteObservation observation, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await partition.Consensus.StateAsync(cancellationToken).ConfigureAwait(false);
            observation.Capture(state);
            if (state.TransportReady && state.LeaderId is not null
                && partition.Configuration.VoterIds.Contains(state.LeaderId, StringComparer.Ordinal)
                && state.Term > BeforeFirstConsensusTerm && state.CommittedIndex > BeforeFirstCommittedEntry
                && state.MaterializedPosition >= state.CommittedIndex
                && observation.Cohort(await node.AcquireCohortAdmissionAsync(cancellationToken).ConfigureAwait(false))
                && (state.Role == ReplicaRole.Follower
                    || state.Role == ReplicaRole.Leader
                    && observation.Leader(await partition.Consensus.IsLeaderAsync(cancellationToken).ConfigureAwait(false))))
            {
                return;
            }
            await Task.Delay(partition.Configuration.HeartbeatInterval, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
