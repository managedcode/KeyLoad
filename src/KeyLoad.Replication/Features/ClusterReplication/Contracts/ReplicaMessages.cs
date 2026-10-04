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
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaEntry)]
public sealed record ReplicaEntry(
    [property: Orleans.Id(0)] long Index,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] ReplicatedOperation? Operation);
/// <summary>Persists the authority, election and committed-prefix metadata of one voter.</summary>
/// <param name="Version">Replica metadata format version.</param>
/// <param name="Incarnation">Authority incarnation of the replica group.</param>
/// <param name="Term">Latest durably observed election term.</param>
/// <param name="VotedFor">Voter selected in this term, or null before voting.</param>
/// <param name="LastIndex">Last retained log or checkpoint position.</param>
/// <param name="CommittedIndex">Durably committed majority prefix.</param>
/// <param name="Snapshot">Published verified checkpoint, or null before checkpointing.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaHardState)]
public sealed record ReplicaHardState(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Term,
    [property: Orleans.Id(3)] string? VotedFor,
    [property: Orleans.Id(4)] long LastIndex,
    [property: Orleans.Id(5)] long CommittedIndex,
    [property: Orleans.Id(6)] ReplicaSnapshot? Snapshot);
/// <summary>Requests a vote with the candidate's latest durable log evidence.</summary>
/// <param name="CandidateId">Configured voter requesting election.</param>
/// <param name="Term">Proposed election term.</param>
/// <param name="LastIndex">Candidate's latest durable position.</param>
/// <param name="LastTerm">Term at the candidate's latest position.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.VoteRequest)]
public sealed record VoteRequest(
    [property: Orleans.Id(0)] string CandidateId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] long LastIndex,
    [property: Orleans.Id(3)] long LastTerm);
/// <summary>Returns the receiver's current durable election decision.</summary>
/// <param name="Term">Receiver's latest observed term.</param>
/// <param name="Granted">Whether this candidate received the durable vote.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.VoteReply)]
public sealed record VoteReply(
    [property: Orleans.Id(0)] long Term,
    [property: Orleans.Id(1)] bool Granted);
/// <summary>Replicates a bounded contiguous suffix after matching previous log evidence.</summary>
/// <param name="LeaderId">Configured voter claiming leadership.</param>
/// <param name="Term">Leader's current term.</param>
/// <param name="PreviousIndex">Position immediately before the proposed suffix.</param>
/// <param name="PreviousTerm">Term required at the previous position.</param>
/// <param name="CommittedIndex">Leader's current committed prefix.</param>
/// <param name="Entries">Ordered entries, or an initialized empty heartbeat batch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.AppendRequest)]
public sealed record AppendRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] long PreviousIndex,
    [property: Orleans.Id(3)] long PreviousTerm,
    [property: Orleans.Id(4)] long CommittedIndex,
    [property: Orleans.Id(5)] ImmutableArray<ReplicaEntry> Entries);
/// <summary>Returns an append acknowledgement or bounded conflict-retry position.</summary>
/// <param name="Term">Receiver's latest observed term.</param>
/// <param name="Accepted">Whether the previous evidence and suffix matched.</param>
/// <param name="MatchedIndex">Last durably matching position.</param>
/// <param name="NextIndex">Next position the leader should send.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.AppendReply)]
public sealed record AppendReply(
    [property: Orleans.Id(0)] long Term,
    [property: Orleans.Id(1)] bool Accepted,
    [property: Orleans.Id(2)] long MatchedIndex,
    [property: Orleans.Id(3)] long NextIndex);
/// <summary>Identifies a materialized current-term majority read cut.</summary>
/// <param name="Incarnation">Replica authority that owns this receipt.</param>
/// <param name="Position">Committed and locally materialized read position.</param>
/// <param name="Term">Term in which the majority barrier was established.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReadBarrierReceipt)]
public sealed record ReadBarrierReceipt(
    [property: Orleans.Id(0)] Guid Incarnation,
    [property: Orleans.Id(1)] long Position,
    [property: Orleans.Id(2)] long Term);
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
    [property: Orleans.Id(8)] bool TransportReady);
