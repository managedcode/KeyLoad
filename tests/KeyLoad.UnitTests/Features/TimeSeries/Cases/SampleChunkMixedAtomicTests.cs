using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkMixedAtomicTests
{
    private const string Collection = "chunk-derived-documents";
    private const string DocumentId = "chunk-summary";
    private const string Json = "{\"source\":\"cpu\",\"count\":2}";
    private const long MissingDocumentRevision = 0;
    private const long FirstDocumentRevision = 1;
    private const long WrongWindowRevision = 1;
    private const long AppendedRevision = 3;
    private const long SealedRevision = 4;
    private const long SealedGeneration = 1;
    private const long SourceSequence = 2;
    private const string MissingAppliedPosition = "The native fixture canonical applied position is absent.";

    [Test]
    public async Task AcChunk008012DocumentAndChunkEffectsRollbackTogetherBeforeHealthyAtomicSeal()
    {
        using var fixture = new SampleChunkCanonicalFixture(nativeReplicaAdmission: true);
        var configureId = Guid.NewGuid();
        fixture.Owner.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(
            fixture.Owner.Partition.TenantId, fixture.Owner.Partition.DatabaseId,
            new ResourceDefinition(Collection, ResourceKind.Collection, fixture.Owner.Partition.TransactionDomainId)),
            SampleChunkCanonicalFixture.Principal, configureId).Get<ResourceDefinition>();
        fixture.Open(); fixture.AppendInitial();
        var documentPrefix = KeySpace.Partition(PartitionRecordFamilies.Document, fixture.Owner.Partition, Collection);
        var documents = SampleRollupWholeFlow.Image(fixture.Owner, documentPrefix);
        var raw = fixture.Raw();
        var request = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new PutDocument(Collection, DocumentId, Json, MissingDocumentRevision, ExplicitReplacement: true),
             new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                 fixture.WindowId, WrongWindowRevision)]);
        var failed = fixture.Owner.Submit(OperationKind.Batch, request,
            SampleChunkCanonicalFixture.Principal, request.CommandId);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(failed.Json).IsNull();
        await Assert.That(SampleRollupWholeFlow.Image(fixture.Owner, documentPrefix)).IsEqualTo(documents);
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        var image = fixture.Image(); var position = fixture.Owner.Store.Position;
        var replay = fixture.Owner.Submit(OperationKind.Batch, request,
            SampleChunkCanonicalFixture.Principal, request.CommandId);
        await Assert.That(SampleRollupWholeFlow.Outcome(replay)).IsEqualTo(SampleRollupWholeFlow.Outcome(failed));
        await Assert.That(fixture.Image()).IsEqualTo(image);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
        var healthy = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new PutDocument(Collection, DocumentId, Json, MissingDocumentRevision, ExplicitReplacement: true),
             new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                 fixture.WindowId, AppendedRevision)]);
        var before = fixture.Owner.Database.EvaluationClock.GetUtcNow();
        var receipt = fixture.Owner.Submit(OperationKind.Batch, healthy, SampleChunkCanonicalFixture.Principal,
            healthy.CommandId).Get<CommitReceipt>();
        var after = fixture.Owner.Database.EvaluationClock.GetUtcNow();
        await DocumentAsync(fixture, healthy.CommandId, receipt, before, after);
        await SampleChunkCanonicalAssertions.Literal(fixture, SealedRevision, SealedGeneration, SourceSequence);
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await Assert.That(SampleRollupWholeFlow.Image(fixture.Owner, documentPrefix) == documents).IsFalse();
    }

    private static async Task DocumentAsync(SampleChunkCanonicalFixture fixture, Guid commandId,
        CommitReceipt receipt, DateTimeOffset before, DateTimeOffset after)
    {
        var reference = new EntityRef(fixture.Owner.Partition, Collection, DocumentId);
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        var applied = fixture.Owner.Store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw new InvalidOperationException(MissingAppliedPosition)));
        await Assert.That(receipt.Token.Position).IsEqualTo(applied);
        var stored = fixture.Owner.Store.Read(view => view.GetRecord<DocumentRecord>(
            KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(reference)));
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.UpdatedAt >= before && stored.UpdatedAt <= after).IsTrue();
        var expected = new DocumentRecord(reference, FirstDocumentRevision, Json,
            new RowAccess(), stored.UpdatedAt, Deleted: false);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(stored)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        var image = fixture.Image(); var position = fixture.Owner.Store.Position;
        var actual = fixture.Owner.Database.GetDocument(SampleChunkCanonicalFixture.Principal,
            reference, receipt.Token);
        var projected = new DocumentResult(reference, FirstDocumentRevision, Json, Redacted: false, []);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(projected)));
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
        await Assert.That(fixture.Image()).IsEqualTo(image);
    }
}
