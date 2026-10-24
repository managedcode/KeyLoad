using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;
using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class AtomicPartitionRosterRegistrationTests
{
    private const string DocumentId = "roster-document";
    private const string DocumentJson = "{\"value\":1}";
    private const long NoStorePosition = 0;
    private const long InvalidDocumentRevision = 99;
    private const string ExistingDocumentId = "existing-document";
    private const string RootPrincipalId = "root";

    [Test]
    public async Task AcBackup004CommandCommitsRegisterPartitionsAndRetryPreservesFirstSeenIdentity()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        var lifetime = fixture.ConfigureGraph();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNull();
        var unknownCommandId = Guid.NewGuid();
        var invalidRequest = new CommandRequest(unknownCommandId,
            AtomicPartitionRosterFixture.Source with { TenantId = string.Empty },
            [new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson)]);
        var unknown = fixture.Submit(OperationKind.Batch, invalidRequest, unknownCommandId);
        var unknownOutcome = fixture.Store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.UnknownOutcome(RootPrincipalId, unknownCommandId)));
        await Assert.That(Errors.Code(unknown.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(unknownOutcome?.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNull();
        var commandId = Guid.NewGuid();
        fixture.WriteLocalDocument(AtomicPartitionRosterFixture.Destination, DocumentId, DocumentJson, lifetime);
        var firstSeenIndex = checked(fixture.Database.LastApplied + 1);
        var source = fixture.Batch(AtomicPartitionRosterFixture.Source, commandId, firstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        await Assert.That(Errors.Code(source.Problem)).IsNull();
        var firstSource = fixture.ReadEntry(AtomicPartitionRosterFixture.Source)!;
        var firstTarget = fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)!;
        var targetFollowUp = fixture.Batch(AtomicPartitionRosterFixture.Destination,
            new PutDocument(AtomicPartitionRosterFixture.Collection, ExistingDocumentId, DocumentJson));
        await Assert.That(firstSource.FirstSeenStorePosition).IsEqualTo(NoStorePosition);
        await Assert.That(firstSource.FirstSeenAppliedIndex).IsEqualTo(firstSeenIndex);
        await Assert.That(firstTarget.FirstSeenStorePosition).IsGreaterThan(NoStorePosition);
        await Assert.That(Errors.Code(targetFollowUp.Problem)).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(firstTarget);

        fixture.Reopen();
        var retry = fixture.Batch(AtomicPartitionRosterFixture.Source, commandId, firstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var reopened = fixture.ReadEntry(AtomicPartitionRosterFixture.Source);
        await Assert.That(Errors.Code(retry.Problem)).IsNull();
        await Assert.That(reopened).IsEqualTo(firstSource);
        var document = fixture.Store.Read(view => view.GetRecord<DocumentRecord>(
            KeySpace.Partition(PartitionRecordFamilies.Document, AtomicPartitionRosterFixture.Source,
                AtomicPartitionRosterFixture.Collection, DocumentId)));
        await Assert.That(document).IsNotNull();
    }

    [Test]
    public async Task AcBackup004ReplicatedRosterBytesIgnoreDifferentNodeLocalPositions()
    {
        using var first = new AtomicPartitionRosterFixture();
        using var second = new AtomicPartitionRosterFixture();
        first.ConfigureGraph();
        var secondLifetime = second.ConfigureGraph();
        second.WriteLocalDocument(AtomicPartitionRosterFixture.Destination, ExistingDocumentId, DocumentJson, secondLifetime);
        var firstPosition = first.Store.Position;
        var secondPosition = second.Store.Position;
        var commandId = Guid.NewGuid();
        var firstSeenIndex = checked(first.Database.LastApplied + 1);
        await Assert.That(second.Database.LastApplied).IsEqualTo(first.Database.LastApplied);
        var firstResult = first.Batch(AtomicPartitionRosterFixture.Source, commandId, firstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var secondResult = second.Batch(AtomicPartitionRosterFixture.Source, commandId, firstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var firstEntry = first.ReadEntry(AtomicPartitionRosterFixture.Source)!;
        var secondEntry = second.ReadEntry(AtomicPartitionRosterFixture.Source)!;

        await Assert.That(firstPosition).IsNotEqualTo(secondPosition);
        await Assert.That(Errors.Code(firstResult.Problem)).IsNull();
        await Assert.That(Errors.Code(secondResult.Problem)).IsNull();
        await Assert.That(firstEntry.FirstSeenStorePosition).IsEqualTo(NoStorePosition);
        await Assert.That(secondEntry.FirstSeenStorePosition).IsEqualTo(NoStorePosition);
        await Assert.That(firstEntry.FirstSeenAppliedIndex).IsEqualTo(firstSeenIndex);
        await Assert.That(secondEntry.FirstSeenAppliedIndex).IsEqualTo(firstSeenIndex);
        await Assert.That(first.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!.AsSpan()
            .SequenceEqual(second.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!)).IsTrue();
        var localFollowUp = first.Batch(AtomicPartitionRosterFixture.Source,
            new PutDocument(AtomicPartitionRosterFixture.Collection, ExistingDocumentId, DocumentJson));
        await Assert.That(Errors.Code(localFollowUp.Problem)).IsNull();
        await Assert.That(first.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!.AsSpan()
            .SequenceEqual(second.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!)).IsTrue();
    }

    [Test]
    public async Task AcBackup004GlobalOutcomesAndGlobalNamedTenantPartitionsUseExactKeyGrammar()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNull();

        var globalPartition = new PartitionRef("global", AtomicPartitionRosterFixture.Source.DatabaseId,
            AtomicPartitionRosterFixture.Source.TransactionDomainId, AtomicPartitionRosterFixture.Source.PartitionKey);
        var unknownPartition = new PartitionRef("unknown", AtomicPartitionRosterFixture.Source.DatabaseId,
            AtomicPartitionRosterFixture.Source.TransactionDomainId, AtomicPartitionRosterFixture.Source.PartitionKey);
        fixture.ConfigureTenantCollection(globalPartition.TenantId, "global-tenant-collection");
        var globalResult = fixture.Batch(globalPartition,
            new PutDocument("global-tenant-collection", DocumentId, DocumentJson));
        fixture.ConfigureTenantCollection(unknownPartition.TenantId, "unknown-tenant-collection");
        var unknownResult = fixture.Batch(unknownPartition,
            new PutDocument("unknown-tenant-collection", DocumentId, DocumentJson));

        await Assert.That(Errors.Code(globalResult.Problem)).IsNull();
        await Assert.That(Errors.Code(unknownResult.Problem)).IsNull();
        await Assert.That(fixture.ReadEntry(globalPartition)).IsNotNull();
        await Assert.That(fixture.ReadEntry(unknownPartition)).IsNotNull();
    }

    [Test]
    public async Task AcBackup004RejectedCommandPersistsOutcomePartitionRosterAfterReset()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, AtomicPartitionRosterFixture.Source,
        [
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson),
            new DeleteDocument(AtomicPartitionRosterFixture.Collection, DocumentId, InvalidDocumentRevision)
        ]);
        var rejected = fixture.Submit(OperationKind.Batch, request, commandId);
        var entry = fixture.ReadEntry(AtomicPartitionRosterFixture.Source);
        var document = fixture.Store.Read(view => view.GetRecord<DocumentRecord>(
            KeySpace.Partition(PartitionRecordFamilies.Document, AtomicPartitionRosterFixture.Source,
                AtomicPartitionRosterFixture.Collection, DocumentId)));
        await Assert.That(Errors.Code(rejected.Problem)).IsNotNull();
        await Assert.That(entry).IsNotNull();
        await Assert.That(document).IsNull();

        fixture.Reopen();
        var replay = fixture.Submit(OperationKind.Batch, request, commandId);
        await ReplayResultAssertions.SameAsync(rejected, replay);
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsEqualTo(entry);
    }

}
