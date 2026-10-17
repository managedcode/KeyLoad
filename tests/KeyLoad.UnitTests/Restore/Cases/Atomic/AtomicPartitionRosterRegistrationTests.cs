using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterRegistrationTests
{
    private const string DocumentId = "roster-document";
    private const string DocumentJson = "{\"value\":1}";
    private const long FirstSeenIndex = 7;
    private const long NoStorePosition = 0;
    private const long InvalidDocumentRevision = 99;
    private const string ExistingDocumentId = "existing-document";
    private const string RootPrincipalId = "root";
    private const string MalformedJson = "{";

    [Test]
    public async Task AcBackup004CommandCommitsRegisterPartitionsAndRetryPreservesFirstSeenIdentity()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNull();
        var unknownCommandId = Guid.NewGuid();
        var unknown = fixture.Database.Apply(new(unknownCommandId, OperationKind.ConfigureResource,
            RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(), MalformedJson));
        var unknownOutcome = fixture.Store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.UnknownOutcome(RootPrincipalId, unknownCommandId)));
        await Assert.That(unknown.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(unknownOutcome?.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNull();
        var commandId = Guid.NewGuid();
        var target = fixture.Batch(AtomicPartitionRosterFixture.Destination,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var source = fixture.Batch(AtomicPartitionRosterFixture.Source, commandId, FirstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        await Assert.That(source.Error).IsNull();
        await Assert.That(target.Error).IsNull();
        var firstSource = fixture.ReadEntry(AtomicPartitionRosterFixture.Source)!;
        var firstTarget = fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)!;
        var targetFollowUp = fixture.Batch(AtomicPartitionRosterFixture.Destination,
            new PutDocument(AtomicPartitionRosterFixture.Collection, ExistingDocumentId, DocumentJson));
        await Assert.That(firstSource.FirstSeenStorePosition).IsEqualTo(NoStorePosition);
        await Assert.That(firstSource.FirstSeenAppliedIndex).IsEqualTo(FirstSeenIndex);
        await Assert.That(firstTarget.FirstSeenStorePosition).IsGreaterThan(NoStorePosition);
        await Assert.That(targetFollowUp.Error).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(firstTarget);

        fixture.Reopen();
        var retry = fixture.Batch(AtomicPartitionRosterFixture.Source, commandId, FirstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var reopened = fixture.ReadEntry(AtomicPartitionRosterFixture.Source);
        await Assert.That(retry.Error).IsNull();
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
        second.ConfigureGraph();
        second.ConfigureAuxiliaryCollection("offset-control");
        var firstPosition = first.Store.Position;
        var secondPosition = second.Store.Position;
        var commandId = Guid.NewGuid();
        var firstResult = first.Batch(AtomicPartitionRosterFixture.Source, commandId, FirstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var secondResult = second.Batch(AtomicPartitionRosterFixture.Source, commandId, FirstSeenIndex,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson));
        var firstEntry = first.ReadEntry(AtomicPartitionRosterFixture.Source)!;
        var secondEntry = second.ReadEntry(AtomicPartitionRosterFixture.Source)!;

        await Assert.That(firstPosition).IsNotEqualTo(secondPosition);
        await Assert.That(firstResult.Error).IsNull();
        await Assert.That(secondResult.Error).IsNull();
        await Assert.That(firstEntry.FirstSeenStorePosition).IsEqualTo(NoStorePosition);
        await Assert.That(secondEntry.FirstSeenStorePosition).IsEqualTo(NoStorePosition);
        await Assert.That(firstEntry.FirstSeenAppliedIndex).IsEqualTo(FirstSeenIndex);
        await Assert.That(secondEntry.FirstSeenAppliedIndex).IsEqualTo(FirstSeenIndex);
        await Assert.That(first.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!.AsSpan()
            .SequenceEqual(second.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!)).IsTrue();
        var localFollowUp = first.Batch(AtomicPartitionRosterFixture.Source,
            new PutDocument(AtomicPartitionRosterFixture.Collection, ExistingDocumentId, DocumentJson));
        await Assert.That(localFollowUp.Error).IsNull();
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

        await Assert.That(globalResult.Error).IsNull();
        await Assert.That(unknownResult.Error).IsNull();
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
        await Assert.That(rejected.Error).IsNotNull();
        await Assert.That(entry).IsNotNull();
        await Assert.That(document).IsNull();

        fixture.Reopen();
        var replay = fixture.Submit(OperationKind.Batch, request, commandId);
        await Assert.That(replay).IsEqualTo(rejected);
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsEqualTo(entry);
    }

}
