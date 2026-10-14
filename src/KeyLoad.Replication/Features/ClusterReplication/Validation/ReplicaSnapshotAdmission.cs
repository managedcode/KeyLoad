using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaSnapshotAdmission
{
    private const int BeforeFirstLogPosition = 0;

    internal static void ValidateCut(IAtomicStore canonical, IDurableReplicaLog log, ReplicaSnapshot snapshot)
    {
        var state = log.State;
        if (snapshot.Index < ReplicaPersistence.AppliedPosition(canonical) || snapshot.Index < (state.Snapshot?.Index ?? BeforeFirstLogPosition)
            || snapshot.Index <= state.CommittedIndex && log.TermAt(snapshot.Index) != snapshot.Term)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidSnapshot);
        }
    }

    internal static void ValidateScope(IAtomicStore canonical, IDurableReplicaLog log, ReplicaConfiguration configuration)
    {
        if (canonical.Identity.Incarnation != configuration.Incarnation || log.State.Incarnation != configuration.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReplicaProtocol.InvalidSnapshot);
        }
    }

}
