using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class NativeSubmissionJournalRead
{
    private const long BeforeFirstEntry = 0;
    private const string ForeignOwner = "The original native journal owner does not match the canonical authority.";
    private const string MissingCommittedEntry = "The original acknowledged journal entry is not retained and committed.";
    private const string WrongTerm = "The original native journal entry does not match the observed term.";

    internal static NativeSubmissionJournalCut Run(ZoneTreeStore replica, DatabaseEngine canonical,
        ReplicaConfiguration configuration, long entryIndex, long entryTerm, CancellationToken cancellationToken,
        StorageReadObserver? additionalReadObserver = null)
    {
        ArgumentNullException.ThrowIfNull(replica);
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryTerm);
        cancellationToken.ThrowIfCancellationRequested();
        RequireOwner(replica, canonical, configuration);
        return replica.Read(view =>
        {
            var budget = new NativeSubmissionJournalReadBudget(additionalReadObserver);
            var originalState = budget.Required(view, ReplicaProtocol.StateStorageKey, cancellationToken);
            var membership = budget.Required(view, ReplicaBenchmarkMembership.StorageKey, cancellationToken);
            var state = ReplicaPersistence.Decode<ReplicaHardState>(originalState, configuration.MaxAppendEntries);
            ReplicaLogValidation.Validate(state, configuration);
            ReplicaBenchmarkMembership.Validate(membership, hasHardState: true, configuration);
            if (entryIndex <= (state.Snapshot?.Index ?? BeforeFirstEntry)
                || entryIndex > state.LastIndex || entryIndex > state.CommittedIndex)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, MissingCommittedEntry); }
            var originalEntry = budget.Required(view, ReplicaProtocol.EntryStorageKey(entryIndex), cancellationToken);
            var entry = ReplicaAdmittedEntryRead.Decode(originalEntry, entryIndex, state.Term,
                configuration.MaxAppendEntries, canonical);
            if (entry.Term != entryTerm)
            { throw Errors.Fail(ErrorCode.Corruption, WrongTerm); }
            cancellationToken.ThrowIfCancellationRequested();
            return new NativeSubmissionJournalCut(replica.Identity.NodeId, replica.Identity.Incarnation,
                replica.Position, originalState, originalEntry, state, entry);
        });
    }

    private static void RequireOwner(ZoneTreeStore replica, DatabaseEngine canonical, ReplicaConfiguration configuration)
    {
        var actual = replica.Identity;
        var authority = canonical.Store.Identity;
        if (actual.Incarnation != configuration.Incarnation || actual.Incarnation != authority.Incarnation
            || !CryptographicOperations.FixedTimeEquals(actual.SigningKey.Span, authority.SigningKey.Span))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, ForeignOwner); }
    }
}
