namespace KeyLoad.Replication;

/// <summary>Identifies the authenticated native replica operation.</summary>
public enum ReplicaRpc
{
    /// <summary>Requests a durable election vote.</summary>
    RequestVote,
    /// <summary>Replicates an ordered entry suffix or heartbeat.</summary>
    Append,
    /// <summary>Forwards an admitted operation to the leader.</summary>
    Forward,
    /// <summary>Acquires a current-term majority read cut.</summary>
    ReadBarrier,
    /// <summary>Begins a bounded verified snapshot transfer.</summary>
    SnapshotBegin,
    /// <summary>Appends an acknowledged snapshot chunk.</summary>
    SnapshotChunk,
    /// <summary>Verifies and installs the completed transfer.</summary>
    SnapshotComplete,
    /// <summary>Confirms an application read cut with an initialized empty append.</summary>
    ReadProbe,
    /// <summary>Acquires a current-term majority cut for trusted native membership.</summary>
    ControlReadBarrier
}
