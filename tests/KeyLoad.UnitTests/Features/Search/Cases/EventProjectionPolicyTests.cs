using KeyLoad.Core.Features.Search;
using KeyLoad.Security;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionPolicyTests
{
    private const string SourceOwner = "projection-source-owner";
    private const string OtherOwner = "projection-other-owner";
    private const string Reclassified = "projection-reclassified";
    private const string NestedPath = "/record/body/value";

    [Test]
    public async Task AcLineage002HiddenDeletedAndRevisedSourcesAreSkippedAtTheCurrentReadCut()
    {
        using var fixture = new EventProjectionFixture(sourceAccess: new(SourceOwner));
        fixture.Apply(fixture.Request());
        fixture.ConfigureReader([EventProjectionFixture.InputUse, EventProjectionFixture.VectorUse],
            restrictRows: true, owner: OtherOwner);
        var hidden = await fixture.SearchAsync();

        fixture.ConfigureReader([EventProjectionFixture.InputUse, EventProjectionFixture.VectorUse],
            restrictRows: true, owner: SourceOwner);
        var visible = await fixture.SearchAsync();
        fixture.Harness.Commit(new PatchDocument(EventProjectionFixture.Collection, EventProjectionFixture.SourceId,
            [new(EventProjectionFixture.InputField, PatchKind.Set, "\"changed\"")], 1));
        var revised = await fixture.SearchAsync();

        await Assert.That(hidden).IsEmpty();
        await Assert.That(visible.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { EventProjectionFixture.TargetId }, CollectionOrdering.Matching);
        await Assert.That(revised).IsEmpty();
    }

    [Test]
    public async Task AcLineage002DeletedSourceIsSkippedAtTheCurrentReadCut()
    {
        using var fixture = new EventProjectionFixture();
        fixture.Apply(fixture.Request());
        fixture.Harness.Commit(new DeleteDocument(EventProjectionFixture.Collection,
            EventProjectionFixture.SourceId, 1));

        var results = await fixture.SearchAsync();

        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task AcLineage002FieldDenialAndCurrentReclassificationExcludeDerivedVectors()
    {
        using var fixture = new EventProjectionFixture();
        fixture.Apply(fixture.Request());
        fixture.ConfigureReader([EventProjectionFixture.VectorUse]);
        var fieldDenied = await fixture.SearchAsync();

        fixture.ConfigureReader([EventProjectionFixture.InputUse, EventProjectionFixture.VectorUse]);
        fixture.ReclassifySource(Reclassified);
        var reclassified = await fixture.SearchAsync();

        await Assert.That(fieldDenied).IsEmpty();
        await Assert.That(reclassified).IsEmpty();
    }

    [Test]
    public async Task AcLineage002RevokedReaderCannotSearchDerivedVectors()
    {
        using var fixture = new EventProjectionFixture();
        fixture.Apply(fixture.Request());
        fixture.ConfigureReader([EventProjectionFixture.InputUse, EventProjectionFixture.VectorUse], revoked: true);

        var error = (await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.SearchAsync))!;

        await Assert.That(error.Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    public async Task AcLineage002UnclassifiedSourceAllowsAdditionalTargetClassifications()
    {
        using var fixture = new EventProjectionFixture(targetClassification: "projection-extra",
            sourceClassification: null);

        var receipt = fixture.Apply(fixture.Request());
        var lineage = fixture.Lineage()!;

        await Assert.That(receipt.Resource).IsEqualTo(EventProjectionFixture.Collection);
        await Assert.That(lineage.SourceClassifications).IsEmpty();
        await Assert.That(lineage.TargetClassifications)
            .IsEquivalentTo(new[] { "projection-extra" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcLineage002ApplyRequiresCurrentPersistedInputUseAndNonrevokedAuthority()
    {
        using var fixture = new EventProjectionFixture();
        fixture.ConfigureWorker(grants: [EventProjectionFixture.InputWrite,
            EventProjectionFixture.VectorUse, EventProjectionFixture.VectorWrite]);

        var missingUse = fixture.ApplyFailure(fixture.Request());

        fixture.ConfigureWorker(revoked: true);
        var revoked = fixture.ApplyFailure(fixture.Request());

        await Assert.That(missingUse.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(fixture.VectorBytes()).IsNull();
        await Assert.That(fixture.Lineage()).IsNull();
    }

    [Test]
    public async Task AcLineage002EffectiveClassificationUsesThePersistedOverlapEvaluator()
    {
        var resource = new ResourceDefinition(EventProjectionFixture.Collection, ResourceKind.Collection, "orders")
        {
            FieldPolicies =
            [
                new(NestedPath, "exact"),
                new("/record", "ancestor"),
                new("/record/body/value/child", "descendant"),
                new("/record/*/value", "wildcard"),
                new("/unrelated", "nonmatch")
            ]
        };
        var classes = VectorProjectionPolicy.Classifications(new AuthorizationPolicy(), resource, NestedPath);

        await Assert.That(classes).IsEquivalentTo(new[] { "ancestor", "descendant", "exact", "wildcard" },
            CollectionOrdering.Matching);
        await Assert.That(VectorProjectionPolicy.TargetIsAtLeastAsRestrictive(classes, [.. classes, "extra-target"]))
            .IsTrue();
        await Assert.That(VectorProjectionPolicy.TargetIsAtLeastAsRestrictive(classes, ["ancestor", "different"]))
            .IsFalse();
    }
}
