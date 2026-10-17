using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ProjectionDigestReclassification
{
    private const string Classification = "projection-digest-reclassified";

    internal static async Task RunAsync(EventProjectionFixture fixture, ApplyVectorProjection request)
    {
        fixture.ReclassifySource(Classification);
        await ProjectionDigestAssertions.HealthyAsync(fixture, schema: 2L, visible: false);
        var key = KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, fixture.TargetCollection);
        var current = fixture.Harness.Store.Read(view => view.GetRecord<ResourceDefinition>(key))!;
        var replacement = current with
        {
            FieldPolicies = [.. current.FieldPolicies.Select(policy => policy.Path == EventProjectionFixture.VectorField
                ? policy with { Classification = Classification } : policy)],
            SchemaVersion = 3L
        };
        var changed = fixture.Harness.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, replacement)
            {
                ExpectedSchemaVersion = current.SchemaVersion
            }).Get<ResourceDefinition>();
        await ProjectionDigestAssertions.EqualAsync(changed, replacement);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, [request]);
        var before = fixture.Harness.Store.Position;
        var result = fixture.Harness.Submit(OperationKind.Batch, command, EventProjectionFixture.WorkerId, command.CommandId);
        await ProjectionDigestAssertions.ReceiptAsync(fixture, command, result.Get<CommitReceipt>(), before);
        var expected = new VectorProjectionLineage(fixture.Stream, 1L, "projection-event",
            new(fixture.Partition, EventProjectionFixture.Collection, EventProjectionFixture.SourceId), 1L,
            EventProjectionFixture.InputField, 3L, 1L, [Classification], fixture.TargetCollection,
            EventProjectionFixture.TargetId, EventProjectionFixture.VectorField, EventProjectionFixture.Space,
            3L, "projection-reducer", "v1", 1L, [Classification]);
        await LineageAsync(fixture.Lineage(), expected);
        await ProjectionDigestAssertions.HealthyAsync(fixture, schema: 3L);
        await ProjectionDigestWholeFlow.ReplayAsync(fixture, command, result);
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        await ProjectionDigestImage.ColdAsync(fixture, image, cut);
        await LineageAsync(fixture.Lineage(), expected);
        await ProjectionDigestAssertions.HealthyAsync(fixture, schema: 3L);
        await ProjectionDigestWholeFlow.ReplayAsync(fixture, command, result);
    }
    private static async Task LineageAsync(VectorProjectionLineage? actual, VectorProjectionLineage expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

}
