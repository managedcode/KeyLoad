using KeyLoad.Core.BackupRestore.Execution;
using KeyLoad.Core.StoragePartitions.Contracts;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class AtomicPartitionRosterBudgetTests
{
    private const long NoAppliedIndex = 0;
    private const long FirstRevision = 1;
    private const byte FirstStagedValue = 1;
    private const string SeedJson = "{}";
    private const string RootPrincipalId = "root";
    private const string CandidateDocumentId = "candidate-document";

    [Test]
    public async Task AcBackup004ResetDiscardsOldCandidatesAndRegistersOnlyRetainedWrites()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        var lifetime = fixture.ConfigureGraph();
        var first = AtomicPartitionRosterFixture.Source;
        var second = AtomicPartitionRosterFixture.Destination;
        var secondRecord = new DocumentRecord(new(second, AtomicPartitionRosterFixture.Collection,
            CandidateDocumentId)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        }, FirstRevision, SeedJson, new RowAccess(), fixture.Database.EvaluationClock.GetUtcNow());
        fixture.Store.Commit((transaction, position) =>
        {
            var bounded = new AtomicPartitionRosterTransaction(transaction, position, NoAppliedIndex);
            bounded.Put(KeySpace.Partition(PartitionRecordFamilies.Document, first,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId), [FirstStagedValue]);
            bounded.Reset();
            bounded.Put(KeySpace.Partition(PartitionRecordFamilies.Document, second,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId), OrleansSerialization.Serialize(secondRecord));
            bounded.PersistCandidates();
            bounded.ValidateCommit();
            return true;
        });
        await Assert.That(fixture.ReadEntry(first)).IsNull();
        await Assert.That(fixture.ReadEntry(second)).IsNotNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(PartitionRecordFamilies.Document, first,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId)))).IsNull();
        var document = fixture.Database.GetDocument(RootPrincipalId, new(second,
            AtomicPartitionRosterFixture.Collection, CandidateDocumentId));
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Json).IsEqualTo(SeedJson);
    }
}
