namespace KeyLoad.Replication;

internal static class ReplicaSerializationAliases
{
    internal const string ReplicaEntry = "keyload.replica.entry.v2";
    internal const string ReplicaEntryBatch = "keyload.replica.batch.v2";
    internal const string ReplicaHardState = "keyload.replica.state.v2";
    internal const string VoteRequest = "keyload.replica.vote-request.v2";
    internal const string VoteReply = "keyload.replica.vote-reply.v2";
    internal const string AppendRequest = "keyload.replica.append-request.v2";
    internal const string AppendReply = "keyload.replica.append-reply.v2";
    internal const string ReadBarrierReceipt = "keyload.replica.read-cut.v2";
    internal const string ReplicaNodeState = "keyload.replica.node-state.v2";
    internal const string ReplicaSnapshot = "keyload.replica.snapshot.v2";
    internal const string SnapshotBeginRequest = "keyload.replica.snapshot-begin.v2";
    internal const string SnapshotChunkRequest = "keyload.replica.snapshot-chunk.v2";
    internal const string SnapshotCompleteRequest = "keyload.replica.snapshot-complete.v2";
    internal const string SnapshotReply = "keyload.replica.snapshot-reply.v2";
    internal const string BenchmarkMembership = "keyload.replica.benchmark-membership.v2";
}
