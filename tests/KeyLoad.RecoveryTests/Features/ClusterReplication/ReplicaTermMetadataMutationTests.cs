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
    private const byte CorruptRecordByte = 0x78;

    [Test]
    public async Task DirectChangeDeleteAndRepairAlwaysRevalidateTheStoredEntry()
    {
        using var fixture = new ReplicaTermMetadataFixture();
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
    }

    [Test]
    public async Task MalformedDirectEntryCannotBeHiddenByThePreviousObservation()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        fixture.Log.SaveTermAndVote(InitialTerm, null);
        fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
        fixture.Log.Commit(FirstIndex);
        await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(ReplicaProtocol.EntryStorageKey(FirstIndex), [CorruptRecordByte]);
            return true;
        });

        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task ReplacingAnUncommittedSuffixHidesItsPreviouslyObservedTerm()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        fixture.Log.SaveTermAndVote(InitialTerm, null);
        fixture.Log.Append([new(FirstIndex, 1, null), new(2, 1, null), new(ThirdIndex, 1, null)]);
        fixture.Log.Commit(FirstIndex);
        await Assert.That(fixture.Log.TermAt(ThirdIndex)).IsEqualTo(1);

        fixture.Log.SaveTermAndVote(InitialTerm, null);
        fixture.Log.Append([new(2, InitialTerm, null)]);

        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(ThirdIndex)).Code)
            .IsEqualTo(ErrorCode.NotFound);
        await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(InitialTerm);
    }

    private static void CommitEntry(ZoneTreeStore store, ReplicaEntry entry)
        => store.Commit((transaction, _) => { transaction.PutRecord(ReplicaProtocol.EntryStorageKey(entry.Index), entry); return true; });

    private static void DeleteEntry(ZoneTreeStore store, long index)
        => store.Commit((transaction, _) => { transaction.Delete(ReplicaProtocol.EntryStorageKey(index)); return true; });
}
