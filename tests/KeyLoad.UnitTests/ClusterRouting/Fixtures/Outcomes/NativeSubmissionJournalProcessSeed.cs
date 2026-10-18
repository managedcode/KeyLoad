using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeSubmissionJournalProcessSeed(CommandRequest Command, ReplicaEntry Entry,
    byte[] NativeEntry, byte[] NativeState, byte[] OutcomeKey, byte[] NativeOutcome, OperationResult Result,
    DocumentResult Document, long PolicyEpoch)
{
    internal static async Task<NativeSubmissionJournalProcessSeed> CreateAsync(C1OutcomeInspectionFixture canonical,
        ZoneTreeStore replica, DurableReplicaLog log, string principalId, long policyEpoch)
    {
        var command = new CommandRequest(Guid.NewGuid(), canonical.Partition,
            [new PutDocument(C1OutcomeInspectionFixture.ResourceName, "journal-owned-document", "{\"journal\":true}")]);
        var operation = canonical.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId, principalId,
            canonical.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var entry = new ReplicaEntry(1, log.State.Term, operation);
        log.Append([entry]);
        log.Commit(entry.Index);
        var result = canonical.Database.Apply(operation, entry.Index);
        await Assert.That(result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(result).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(canonical.Database.ResolveOutcome(operation)))).IsTrue();
        var key = CommandOutcomeKeyResolver.ForNew(principalId, command.CommandId,
            new(CommandOutcomeScopeKind.Partition, command.Partition)).Key;
        var outcome = canonical.Store.Read(view => view.ReadOwnedValue(key)) ?? throw Missing();
        var document = canonical.Database.GetDocument(principalId,
            new(command.Partition, C1OutcomeInspectionFixture.ResourceName, "journal-owned-document")) ?? throw Missing();
        await Assert.That(document.Json).IsEqualTo("{\"journal\":true}");
        var originalEntry = replica.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(entry.Index)))
            ?? throw Missing();
        await Assert.That(originalEntry.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(entry))).IsTrue();
        return new(command, entry, originalEntry, ReplicaProtocolCodec.Serialize(log.State), key, outcome, result, document, policyEpoch);
    }

    internal async Task RequireCanonicalAsync(C1OutcomeInspectionFixture canonical)
    {
        var cut = NativeSubmissionJournalProcessCut.WithCanonical(canonical, store =>
        {
            var original = store.Read(view => view.ReadOwnedValue(OutcomeKey)) ?? throw Missing();
            var database = C1OutcomeInspectionHealthyFollowUp.CreateDatabaseEngine(store);
            var document = database.GetDocument(Entry.Operation!.PrincipalId,
                new(Command.Partition, C1OutcomeInspectionFixture.ResourceName, "journal-owned-document"));
            return (Outcome: original, Document: document);
        });
        var outcome = cut.Outcome;
        await Assert.That(outcome.AsSpan().SequenceEqual(NativeOutcome)).IsTrue();
        var stored = NativeSerialization.Deserialize<StoredOutcome>(outcome);
        await Assert.That(stored.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(Entry.Operation!));
        await Assert.That(stored.PolicyEpoch).IsEqualTo(PolicyEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(canonical.Incarnation);
        await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(Command.Partition);
        await Assert.That(NativeSerialization.Serialize(stored.Result).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(Result))).IsTrue();
        await Assert.That(stored.BlobAuthority).IsNull();
        await Assert.That(stored.CompositionAuthority).IsNull();
        await Assert.That(stored.OnlineTextAuthority).IsNull();
        await Assert.That(stored.RemoteTransferFailureAuthority).IsNull();
        await Assert.That(stored.RemoteTransferAuthority).IsNull();
        var actual = cut.Document;
        await Assert.That(actual is not null && NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(Document))).IsTrue();
    }

    private static InvalidOperationException Missing() => new("The genuine original journal seed is missing.");
    public override string ToString() => nameof(NativeSubmissionJournalProcessSeed);
}
