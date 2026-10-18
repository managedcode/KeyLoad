using KeyLoad.Core;
using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterBudgetTests
{
    private const long NoAppliedIndex = 0;
    private const long FirstRevision = 1;
    private const byte FirstStagedValue = 1;
    private const byte SecondStagedValue = 2;
    private const int OneCandidate = 1;
    private const int OriginalEncodedRosterCandidateByteBudget = 88;
    private const string SeedJson = "{}";
    private const string RootPrincipalId = "root";
    private const string ExistingDocumentId = "existing-document";
    private const string CandidateDocumentId = "candidate-document";
    private const string BudgetExhausted = "The operation exceeds its partition roster candidate budget.";

    [Test]
    public async Task AcBackup004RealZoneTreeCandidateBudgetRejectsWholeStagedImage()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var boundedLimits = new DatabaseLimits { MaxBatchMutations = OneCandidate };
        boundedLimits.Validate();
        var boundedLimitOptions = Options.Create(boundedLimits);
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        var first = AtomicPartitionRosterFixture.Source;
        var second = AtomicPartitionRosterFixture.Destination;
        var original = fixture.Batch(first,
            new PutDocument(AtomicPartitionRosterFixture.Collection, ExistingDocumentId, SeedJson));
        var originalEntry = fixture.ReadEntry(first);
        var originalPosition = fixture.Store.Position;
        var originalJournal = await File.ReadAllBytesAsync(fixture.JournalPath, token);
        await Assert.That(Errors.Code(original.Problem)).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((transaction, position) =>
        {
            var bounded = new AtomicPartitionRosterTransaction(transaction, boundedLimitOptions, position, NoAppliedIndex);
            bounded.Put(KeySpace.Partition(PartitionRecordFamilies.Document, first,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId), [FirstStagedValue]);
            bounded.Put(KeySpace.Partition(PartitionRecordFamilies.Document, second,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId), [SecondStagedValue]);
            bounded.PersistCandidates();
            bounded.ValidateCommit();
            return true;
        }));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).IsEqualTo(BudgetExhausted);
        await Assert.That(fixture.Store.Position).IsEqualTo(originalPosition);
        var unchangedJournal = await File.ReadAllBytesAsync(fixture.JournalPath, token);
        await Assert.That(unchangedJournal.AsSpan().SequenceEqual(originalJournal)).IsTrue();
        await Assert.That(fixture.ReadEntry(first)).IsEqualTo(originalEntry);
        await Assert.That(fixture.ReadEntry(second)).IsNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(PartitionRecordFamilies.Document, first,
                AtomicPartitionRosterFixture.Collection, ExistingDocumentId)))).IsNotNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(PartitionRecordFamilies.Document, first,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId)))).IsNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(PartitionRecordFamilies.Document, second,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId)))).IsNull();
    }

    [Test]
    public async Task AcBackup004EncodedEmptyPlacementCandidateByteBudgetRejectsWholeStagedImage()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new AtomicPartitionRosterFixture();
        var first = AtomicPartitionRosterFixture.Source;
        var second = AtomicPartitionRosterFixture.Destination;
        var maximumSingleKeyBytes = Math.Max(AtomicPartitionRosterKeys.Partition(first).Length,
            AtomicPartitionRosterKeys.Partition(second).Length);
        await Assert.That(maximumSingleKeyBytes).IsEqualTo(OriginalEncodedRosterCandidateByteBudget);
        var boundedLimits = new DatabaseLimits { MaxBatchBytes = maximumSingleKeyBytes };
        boundedLimits.Validate();
        var boundedLimitOptions = Options.Create(boundedLimits);
        var firstKey = KeySpace.Partition(PartitionRecordFamilies.Document, first,
            AtomicPartitionRosterFixture.Collection, CandidateDocumentId);
        var secondKey = KeySpace.Partition(PartitionRecordFamilies.Document, second,
            AtomicPartitionRosterFixture.Collection, CandidateDocumentId);
        var originalPosition = fixture.Store.Position;
        var originalJournal = await File.ReadAllBytesAsync(fixture.JournalPath, token);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((transaction, position) =>
        {
            var bounded = new AtomicPartitionRosterTransaction(transaction, boundedLimitOptions, position, NoAppliedIndex);
            bounded.Put(firstKey, [FirstStagedValue]);
            bounded.Put(secondKey, [SecondStagedValue]);
            return true;
        }));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).IsEqualTo(BudgetExhausted);
        await Assert.That(fixture.Store.Position).IsEqualTo(originalPosition);
        var unchangedJournal = await File.ReadAllBytesAsync(fixture.JournalPath, token);
        await Assert.That(unchangedJournal.AsSpan().SequenceEqual(originalJournal)).IsTrue();
        await Assert.That(fixture.ReadEntry(first)).IsNull();
        await Assert.That(fixture.ReadEntry(second)).IsNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(firstKey))).IsNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(secondKey))).IsNull();
    }

    [Test]
    public async Task AcBackup004ResetDiscardsOldCandidatesAndRegistersOnlyRetainedWrites()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        var lifetime = fixture.ConfigureGraph();
        var limits = new DatabaseLimits();
        limits.Validate();
        var limitOptions = Options.Create(limits);
        var first = AtomicPartitionRosterFixture.Source;
        var second = AtomicPartitionRosterFixture.Destination;
        var secondRecord = new DocumentRecord(new(second, AtomicPartitionRosterFixture.Collection,
            CandidateDocumentId)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        }, FirstRevision, SeedJson, new RowAccess(), fixture.Database.EvaluationClock.GetUtcNow());
        fixture.Store.Commit((transaction, position) =>
        {
            var bounded = new AtomicPartitionRosterTransaction(transaction, limitOptions, position, NoAppliedIndex);
            bounded.Put(KeySpace.Partition(PartitionRecordFamilies.Document, first,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId), [FirstStagedValue]);
            bounded.Reset();
            bounded.Put(KeySpace.Partition(PartitionRecordFamilies.Document, second,
                AtomicPartitionRosterFixture.Collection, CandidateDocumentId), NativeSerialization.Serialize(secondRecord));
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
