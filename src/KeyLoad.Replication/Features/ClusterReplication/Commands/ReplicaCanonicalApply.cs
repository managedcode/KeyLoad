using KeyLoad.Core;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaCanonicalApply
{
    private const int ContiguousIndexStep = 1;

    private static readonly byte[] AppliedStorageKey = KeySpace.Applied.ToArray();

    internal static void ApplyBatch(DatabaseEngine database, IDurableReplicaLog log, int batchSize,
        Func<ReplicaEntry, IDisposable?>? canonicalObservation = null)
    {
        var started = DatabasePhaseTelemetry.Begin();
        var outcome = DatabasePhaseOutcome.Faulted;
        try
        {
            var cut = Math.Min(log.State.CommittedIndex, checked(database.LastApplied + batchSize));
            for (var index = database.LastApplied + ContiguousIndexStep; index <= cut; index++)
            {
                var entry = log.ReadEntry(index) ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
                ApplyEntry(database, entry, canonicalObservation);
            }
            outcome = DatabasePhaseOutcome.Completed;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.CanonicalApplyBatch, outcome, started);
        }
    }

    private static void ApplyEntry(DatabaseEngine database, ReplicaEntry entry,
        Func<ReplicaEntry, IDisposable?>? canonicalObservation)
    {
        if (entry.Operation is not { } operation)
        {
            database.Store.Commit((transaction, _) => { transaction.PutRecord(AppliedStorageKey, entry.Index); return true; });
            return;
        }
        if (canonicalObservation is null)
        {
            database.Apply(operation, entry.Index);
            return;
        }
        ReplicaObservedCanonicalApply.Apply(database, entry, canonicalObservation);
    }

    internal static long Begin(IReplicaSnapshotStore snapshots, ReplicaSnapshot snapshot)
    {
        try
        {
            return snapshots.Begin(snapshot);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Conflict && error.Message == ReplicaProtocol.SnapshotUnavailable)
        {
            snapshots.ResetIncoming();
            return snapshots.Begin(snapshot);
        }
    }
}
