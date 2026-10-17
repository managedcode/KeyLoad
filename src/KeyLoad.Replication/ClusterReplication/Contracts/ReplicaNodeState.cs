namespace KeyLoad.Replication;

/// <summary>Provides an observed physical voter's replica status.</summary>
/// <param name="VoterId">Configured local voter identity.</param>
/// <param name="LeaderId">Observed leader, or null before election.</param>
/// <param name="Role">Local consensus role.</param>
/// <param name="Term">Latest durable term.</param>
/// <param name="LastIndex">Last retained log position.</param>
/// <param name="CommittedIndex">Durably committed prefix.</param>
/// <param name="MaterializedPosition">Locally applied canonical prefix.</param>
/// <param name="SnapshotIndex">Published checkpoint position, or zero before checkpointing.</param>
/// <param name="TransportReady">Whether the node's native replica service is attached.</param>
/// <param name="HasCheckpointPrefix"></param>
/// <param name="CheckpointPending"></param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaNodeState)]
public sealed record ReplicaNodeState(
    [property: Orleans.Id(0)] string VoterId,
    [property: Orleans.Id(1)] string? LeaderId,
    [property: Orleans.Id(2)] ReplicaRole Role,
    [property: Orleans.Id(3)] long Term,
    [property: Orleans.Id(4)] long LastIndex,
    [property: Orleans.Id(5)] long CommittedIndex,
    [property: Orleans.Id(6)] long MaterializedPosition,
    [property: Orleans.Id(7)] long SnapshotIndex,
    [property: Orleans.Id(8)] bool TransportReady,
    [property: Orleans.Id(9)] bool? HasCheckpointPrefix = null,
    [property: Orleans.Id(10)] bool? CheckpointPending = null);
