using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeJournal
{
    internal static (ReplicaHardState State, ReplicaEntry[] Entries) Read(string[] rows,
        ReplicaConfiguration configuration, DatabaseEngine canonical, long applied, C1SubmissionJournalInspectionReadBudget budget, CancellationToken token)
    {
        var state = ReplicaPersistence.Decode<ReplicaHardState>(NativeBlobWholeRows.Required(rows,
            ReplicaProtocol.StateStorageKey, budget), configuration.MaxAppendEntries);
        ReplicaLogValidation.Validate(state, configuration);
        ReplicaBenchmarkMembership.Validate(NativeBlobWholeRows.Required(rows,
            ReplicaBenchmarkMembership.StorageKey, budget), true, configuration);
        if (state.Snapshot is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, "The full original Blob journal is compacted; no missing authority is inferred."); }
        if (applied < 0 || applied != state.CommittedIndex || state.LastIndex > NativeBlobWholeRows.MaximumRecords)
        { throw Errors.Fail(ErrorCode.Corruption, "The complete original Blob journal/applied positions disagree."); }
        var entries = new List<ReplicaEntry>();
        long previousTerm = 0;
        for (long index = 1; index <= state.LastIndex; index++)
        {
            token.ThrowIfCancellationRequested();
            var bytes = NativeBlobWholeRows.Required(rows, ReplicaProtocol.EntryStorageKey(index), budget);
            var entry = ReplicaAdmittedEntryRead.Decode(bytes, index, state.Term,
                configuration.MaxAppendEntries, canonical);
            if (entry.Term < previousTerm || bytes.Length > configuration.MaxAppendBytes)
            { throw Errors.Fail(ErrorCode.Corruption, "The complete original Blob journal term or append budget is invalid."); }
            previousTerm = entry.Term;
            entries.Add(entry);
        }
        return (state, entries.ToArray());
    }
}
