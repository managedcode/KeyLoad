using System.Text;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataMutationTests
{
    private const long InitialTerm = 2;
    private const long OriginalEntryTerm = 1;
    private const long ChangedEntryTerm = 2;
    private const int FirstIndex = 1;
    private const int ThirdIndex = 3;
    private const string ValidDocumentJson = "{}";
    private const string BatchKindToken = "\"kind\":\"Batch\"";
    private const string UnknownOperationField = "\"unknown\":true,";

    [Test]
    public async Task DirectChangeDeleteAndRepairAlwaysRevalidateTheStoredEntry()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            var before = fixture.Store.GetReadDiagnostics();

            CommitEntry(fixture.Store, new(FirstIndex, ChangedEntryTerm, null));
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(ChangedEntryTerm);
            DeleteEntry(fixture.Store, FirstIndex);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
                .IsEqualTo(ErrorCode.Corruption);
            CommitEntry(fixture.Store, new(FirstIndex, ChangedEntryTerm, null));
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(ChangedEntryTerm);

            var after = fixture.Store.GetReadDiagnostics();
            await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(3);
        });
    }

    [Test]
    public async Task MalformedNestedOperationCannotBeSkippedOnTheStrictTermMiss()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            var operation = ReplicaTermMetadataFixture.Operation(ReplicaTermMetadataFixture.AtomicBatch(ValidDocumentJson));
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, operation)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            var validJson = Encoding.UTF8.GetString(ReplicaProtocolCodec.Serialize(new ReplicaEntry(FirstIndex, OriginalEntryTerm, operation)));
            await Assert.That(validJson.Contains(BatchKindToken, StringComparison.Ordinal)).IsTrue();
            var malformed = Encoding.UTF8.GetBytes(validJson.Replace(BatchKindToken,
                UnknownOperationField + BatchKindToken, StringComparison.Ordinal));
            fixture.Store.Commit((transaction, _) =>
            {
                transaction.Put(ReplicaProtocol.EntryStorageKey(FirstIndex), malformed);
                return true;
            });

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
                .IsEqualTo(ErrorCode.Corruption);
        });
    }

    [Test]
    [Arguments(2, 1)]
    [Arguments(1, 0)]
    [Arguments(1, 3)]
    public async Task StoredIndexAndTermMustRemainValidOnEveryMiss(long storedIndex, long storedTerm)
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            CommitEntryAtRequestedKey(fixture.Store, FirstIndex, new(storedIndex, storedTerm, null));

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
                .IsEqualTo(ErrorCode.Corruption);
        });
    }

    [Test]
    public async Task ReplacingAnUncommittedSuffixHidesItsPreviouslyObservedTerm()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(FirstIndex, 1, null), new(2, 1, null), new(ThirdIndex, 1, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(ThirdIndex)).IsEqualTo(1);

            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(2, InitialTerm, null)]);

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(ThirdIndex)).Code)
                .IsEqualTo(ErrorCode.NotFound);
            await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(InitialTerm);
        });
    }

    [Test]
    public async Task NormalTermAdvanceAndCommitCannotReuseThePreviousCutObservation()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(OriginalEntryTerm, null);
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            var beforeAdvance = fixture.Store.GetReadDiagnostics();

            fixture.Log.SaveTermAndVote(InitialTerm, ReplicaTermMetadataFixture.VoterA);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            fixture.Log.Append([new(2, InitialTerm, null)]);
            fixture.Log.Commit(2);

            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(InitialTerm);
            var afterAdvance = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterAdvance.BorrowedPointLookups - beforeAdvance.BorrowedPointLookups).IsEqualTo(3);
        });
    }

    private static void CommitEntry(ZoneTreeStore store, ReplicaEntry entry)
        => store.Commit((transaction, _) => { transaction.PutRecord(ReplicaProtocol.EntryStorageKey(entry.Index), entry); return true; });

    private static void CommitEntryAtRequestedKey(ZoneTreeStore store, long requestedIndex, ReplicaEntry entry)
        => store.Commit((transaction, _) => { transaction.PutRecord(ReplicaProtocol.EntryStorageKey(requestedIndex), entry); return true; });

    private static void DeleteEntry(ZoneTreeStore store, long index)
        => store.Commit((transaction, _) => { transaction.Delete(ReplicaProtocol.EntryStorageKey(index)); return true; });
}
