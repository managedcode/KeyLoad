namespace KeyLoad.Replication;

internal static class ReplicaCheckpointPublication
{
    private const long BeforeFirstLogPosition = 0;

    internal static ReplicaHardState Next(ReplicaSnapshot snapshot, ReplicaHardState state,
        ReplicaConfiguration configuration, Func<long, long> termAt)
    {
        ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
        if (snapshot.Index < (state.Snapshot?.Index ?? BeforeFirstLogPosition))
        { throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot); }
        var retain = snapshot.Index <= state.LastIndex && termAt(snapshot.Index) == snapshot.Term;
        if (!retain && snapshot.Index <= state.CommittedIndex)
        { throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot); }
        return state with
        {
            Snapshot = snapshot,
            LastIndex = retain ? state.LastIndex : snapshot.Index,
            CommittedIndex = Math.Max(state.CommittedIndex, snapshot.Index),
            Term = Math.Max(state.Term, snapshot.Term),
            VotedFor = snapshot.Term > state.Term ? null : state.VotedFor
        };
    }
}
