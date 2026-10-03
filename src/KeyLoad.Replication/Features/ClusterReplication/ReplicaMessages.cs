using System.Collections.Immutable;

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

/// <summary>Identifies a durable boundary observed by real process-recovery tests.</summary>
public enum ReplicaCrashBoundary
{
    /// <summary>Term and vote have crossed the durable barrier.</summary>
    TermSaved,
    /// <summary>The ordered log suffix has crossed the durable barrier.</summary>
    EntryAcknowledged,
    /// <summary>The commit position has crossed the durable barrier.</summary>
    CommitAcknowledged,
    /// <summary>The complete incoming snapshot has been verified.</summary>
    SnapshotVerified,
    /// <summary>The verified canonical cut has been installed.</summary>
    SnapshotInstalled,
    /// <summary>The incoming transfer descriptor has been published.</summary>
    SnapshotTransferBegun,
    /// <summary>The incoming chunk has been durably acknowledged.</summary>
    SnapshotChunkAcknowledged,
    /// <summary>The incoming image failed verification before cleanup.</summary>
    SnapshotRejected,
    /// <summary>The installed image and replica snapshot metadata have been published.</summary>
    SnapshotPublished
}

/// <summary>Owns an ordered replica entry or current-term establishment noop.</summary>
/// <param name="Index">Monotonic position in the group log.</param>
/// <param name="Term">Election term that created the entry.</param>
/// <param name="Operation">Canonical database operation, or null for a noop.</param>
public sealed record ReplicaEntry(long Index, long Term, ReplicatedOperation? Operation);
/// <summary>Persists the authority, election and committed-prefix metadata of one voter.</summary>
/// <param name="Version">Replica metadata format version.</param>
/// <param name="Incarnation">Authority incarnation of the replica group.</param>
/// <param name="Term">Latest durably observed election term.</param>
/// <param name="VotedFor">Voter selected in this term, or null before voting.</param>
/// <param name="LastIndex">Last retained log or checkpoint position.</param>
/// <param name="CommittedIndex">Durably committed majority prefix.</param>
/// <param name="Snapshot">Published verified checkpoint, or null before checkpointing.</param>
public sealed record ReplicaHardState(int Version, Guid Incarnation, long Term, string? VotedFor, long LastIndex,
    long CommittedIndex, ReplicaSnapshot? Snapshot);
/// <summary>Requests a vote with the candidate's latest durable log evidence.</summary>
/// <param name="CandidateId">Configured voter requesting election.</param>
/// <param name="Term">Proposed election term.</param>
/// <param name="LastIndex">Candidate's latest durable position.</param>
/// <param name="LastTerm">Term at the candidate's latest position.</param>
public sealed record VoteRequest(string CandidateId, long Term, long LastIndex, long LastTerm);
/// <summary>Returns the receiver's current durable election decision.</summary>
/// <param name="Term">Receiver's latest observed term.</param>
/// <param name="Granted">Whether this candidate received the durable vote.</param>
public sealed record VoteReply(long Term, bool Granted);
/// <summary>Replicates a bounded contiguous suffix after matching previous log evidence.</summary>
/// <param name="LeaderId">Configured voter claiming leadership.</param>
/// <param name="Term">Leader's current term.</param>
/// <param name="PreviousIndex">Position immediately before the proposed suffix.</param>
/// <param name="PreviousTerm">Term required at the previous position.</param>
/// <param name="CommittedIndex">Leader's current committed prefix.</param>
/// <param name="Entries">Ordered entries, or an initialized empty heartbeat batch.</param>
public sealed record AppendRequest(string LeaderId, long Term, long PreviousIndex, long PreviousTerm, long CommittedIndex,
    ImmutableArray<ReplicaEntry> Entries);
/// <summary>Returns an append acknowledgement or bounded conflict-retry position.</summary>
/// <param name="Term">Receiver's latest observed term.</param>
/// <param name="Accepted">Whether the previous evidence and suffix matched.</param>
/// <param name="MatchedIndex">Last durably matching position.</param>
/// <param name="NextIndex">Next position the leader should send.</param>
public sealed record AppendReply(long Term, bool Accepted, long MatchedIndex, long NextIndex);
/// <summary>Identifies a materialized current-term majority read cut.</summary>
/// <param name="Incarnation">Replica authority that owns this receipt.</param>
/// <param name="Position">Committed and locally materialized read position.</param>
/// <param name="Term">Term in which the majority barrier was established.</param>
public sealed record ReadBarrierReceipt(Guid Incarnation, long Position, long Term);
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
public sealed record ReplicaNodeState(string VoterId, string? LeaderId, ReplicaRole Role, long Term, long LastIndex,
    long CommittedIndex, long MaterializedPosition, long SnapshotIndex, bool TransportReady);
