using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionReplayTests
{
    [Test]
    public async Task AcLineage003ChangedOutputConflictsAndOrdinaryVectorReplacementClearsLineageAndEffect()
    {
        using var fixture = new EventProjectionFixture();
        var request = fixture.Request();
        var effectKey = VectorProjectionKeys.Effect(fixture.Partition, request);
        fixture.Apply(request);
        var before = fixture.VectorBytes()!;

        var changed = fixture.ApplyFailure(fixture.Request([0, 1]));
        fixture.Harness.Commit(new PutVector(EventProjectionFixture.Collection, EventProjectionFixture.TargetId,
            EventProjectionFixture.VectorField, [0, 1], EventProjectionFixture.Space, 1));
        var lineage = fixture.Lineage();
        var effect = fixture.Harness.Store.Read(view => view.GetRecord<VectorProjectionEffect>(effectKey));
        var vector = fixture.VectorBytes()!;
        var search = await fixture.SearchAsync();

        await Assert.That(changed.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(lineage).IsNull();
        await Assert.That(effect).IsNull();
        await Assert.That(vector.SequenceEqual(before)).IsFalse();
        await Assert.That(search.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { EventProjectionFixture.TargetId }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcLineage003NativeReopenPreservesEffectLineageAndStableReceipt()
    {
        using var fixture = new EventProjectionFixture();
        var request = fixture.Request();
        var receipt = fixture.Apply(request);
        var persistedVector = fixture.VectorBytes()!;
        fixture.Harness.Reopen();

        var lineage = fixture.Lineage();
        var retry = fixture.Apply(request);
        var search = await fixture.SearchAsync();

        await Assert.That(lineage).IsNotNull();
        await Assert.That(NativeSerialization.Serialize(retry).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(fixture.VectorBytes()!.SequenceEqual(persistedVector)).IsTrue();
        await Assert.That(search.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { EventProjectionFixture.TargetId }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReauthorizationAcceptsACommittedReceiptAfterSourceRevisionChangesButSearchSkipsIt()
    {
        using var fixture = new EventProjectionFixture();
        var request = fixture.Request();
        fixture.Apply(request);
        fixture.Harness.Commit(new PatchDocument(EventProjectionFixture.Collection, EventProjectionFixture.SourceId,
            [new(EventProjectionFixture.InputField, PatchKind.Set, "\"new revision\"")], 1));

        fixture.Reauthorize(request);
        var search = await fixture.SearchAsync();

        await Assert.That(search).IsEmpty();
        var retry = fixture.ApplyFailure(request);
        await Assert.That(retry.Code).IsEqualTo(ErrorCode.RevisionConflict);
    }
}
