using System.Text;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionInputTests
{
    private const string WrongEventId = "different-event";
    private const string AlternateStreamSet = "projection-alternate-events";

    [Test]
    public async Task AcLineage001AppliesExactInputAndRetriesTheSameReceiptWithoutChangingTheVector()
    {
        using var fixture = new EventProjectionFixture();
        var request = fixture.Request();
        var receipt = fixture.Apply(request);
        var originalVector = fixture.VectorBytes()!;
        var lineage = fixture.Lineage()!;
        var nativeLineage = Encoding.UTF8.GetString(NativeSerialization.Serialize(lineage));

        var retry = fixture.Apply(request);
        var search = await fixture.SearchAsync();

        await Assert.That(lineage.SourceStream).IsEqualTo(fixture.Stream);
        await Assert.That(lineage.SourceEventRevision).IsEqualTo(1L);
        await Assert.That(lineage.SourceEventId).IsEqualTo("projection-event");
        await Assert.That(lineage.SourceDocument).IsEqualTo(new EntityRef(fixture.Partition,
            EventProjectionFixture.Collection, EventProjectionFixture.SourceId));
        await Assert.That(lineage.SourceDocumentRevision).IsEqualTo(1L);
        await Assert.That(lineage.InputField).IsEqualTo(EventProjectionFixture.InputField);
        await Assert.That(lineage.ReducerGeneration).IsEqualTo(1L);
        await Assert.That(nativeLineage.Contains(EventProjectionFixture.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(NativeSerialization.Serialize(retry).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(fixture.VectorBytes()!.SequenceEqual(originalVector)).IsTrue();
        await Assert.That(search.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { EventProjectionFixture.TargetId }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcLineage001AllowsDistinctCanonicalSourceAndTargetCollectionsInOnePartition()
    {
        using var fixture = new EventProjectionFixture(separateTarget: true);

        var receipt = fixture.Apply(fixture.Request());
        var lineage = fixture.Lineage()!;
        var search = await fixture.SearchAsync();

        await Assert.That(lineage.SourceDocument.Collection).IsEqualTo(EventProjectionFixture.Collection);
        await Assert.That(lineage.TargetCollection).IsEqualTo(EventProjectionFixture.SeparateTargetCollection);
        await Assert.That(lineage.SourceDocument.Partition).IsEqualTo(fixture.Partition);
        await Assert.That(receipt.Resource).IsEqualTo(EventProjectionFixture.SeparateTargetCollection);
        await Assert.That(search.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { EventProjectionFixture.TargetId }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcLineage001RejectsWrongEventIdentityAndChangedSourceBeforeAnyVectorWrite()
    {
        using var fixture = new EventProjectionFixture();
        var vectorKey = fixture.VectorKey();
        var before = fixture.Harness.Store.Read(view => view.ReadOwnedValue(vectorKey));

        var wrongEvent = fixture.ApplyFailure(fixture.Request(eventId: WrongEventId));
        fixture.Harness.Commit(new PatchDocument(EventProjectionFixture.Collection, EventProjectionFixture.SourceId,
            [new(EventProjectionFixture.InputField, PatchKind.Set, "\"changed source\"")], 1));
        var staleSource = fixture.ApplyFailure(fixture.Request());
        var after = fixture.Harness.Store.Read(view => view.ReadOwnedValue(vectorKey));

        await Assert.That(wrongEvent.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(staleSource.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(before).IsNull();
        await Assert.That(after).IsNull();
        await Assert.That(fixture.Lineage()).IsNull();
    }

    [Test]
    public async Task AcLineage001RejectsMissingWrongStreamAndOldGenerationEventsAtomically()
    {
        using var fixture = new EventProjectionFixture();
        fixture.Harness.Configure(AlternateStreamSet, ResourceKind.StreamSet);
        fixture.Harness.Commit(new AppendEvents(AlternateStreamSet, EventProjectionFixture.StreamId,
            [new EventData("alternate-event", "SourceUpdated", "{}")], ExpectedStreamRevision.NoStream));
        fixture.ConfigureWorker(additionalStreams: [AlternateStreamSet]);

        var missing = fixture.ApplyFailure(fixture.Request(sourceRevision: 2));
        var wrongStream = fixture.ApplyFailure(fixture.Request() with
        {
            SourceStream = new(fixture.Partition, AlternateStreamSet, EventProjectionFixture.StreamId, 1)
        });
        var oldGeneration = fixture.ApplyFailure(fixture.Request() with
        {
            SourceStream = fixture.Stream with { Generation = 2 }
        });

        await Assert.That(missing.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(wrongStream.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(oldGeneration.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(fixture.VectorBytes()).IsNull();
        await Assert.That(fixture.Lineage()).IsNull();
    }

    [Test]
    public async Task AcLineage002RejectsAWeakerTargetClassificationAtomically()
    {
        using var fixture = new EventProjectionFixture(targetClassification: "projection-public");

        var error = fixture.ApplyFailure(fixture.Request());

        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.VectorBytes()).IsNull();
        await Assert.That(fixture.Lineage()).IsNull();
    }
}
