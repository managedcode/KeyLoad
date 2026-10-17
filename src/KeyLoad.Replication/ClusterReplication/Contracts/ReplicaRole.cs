namespace KeyLoad.Replication;

/// <summary>Identifies a voter's current consensus role.</summary>
public enum ReplicaRole
{
    /// <summary>Accepts valid leader replication.</summary>
    Follower,
    /// <summary>Requests a majority vote in the current election term.</summary>
    Candidate,
    /// <summary>Orders commands and establishes current-term quorum barriers.</summary>
    Leader
}
