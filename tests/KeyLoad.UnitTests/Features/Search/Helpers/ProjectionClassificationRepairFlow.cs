using KeyLoad.Core.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ProjectionClassificationRepairFlow
{
    private const long OriginalSchema = 1;
    private const long RepairedSchema = 2;
    private const long OriginalRevision = 1;
    private const long OriginalWorkerEpoch = 1;
    private const long ReducerGeneration = 1;
    private const string WeakerClass = "projection-public";
    private const string EventId = "projection-event";
    private const string ReducerId = "projection-reducer";
    private const string ReducerVersion = "v1";
    private const string NativeDirectory = "classification-repair-native-text";
    private const string MissingResource = "The original classification resource is missing.";
    private const string AcceptedFailedCommand = "The immutable denied projection command was accepted.";
    private const string MissingLineage = "The repaired projection lineage is missing.";
    private const string MissingVector = "The repaired projection vector is missing.";

    internal static KeyLoadException Refused(EventProjectionFixture fixture, ApplyVectorProjection request,
        Guid commandId)
        => Assert.ThrowsExactly<KeyLoadException>(() => fixture.Harness.Submit(OperationKind.Batch,
            new CommandRequest(commandId, fixture.Partition, [request]), EventProjectionFixture.WorkerId,
            commandId).Get<CommitReceipt>()) ?? throw new InvalidOperationException(AcceptedFailedCommand);

    internal static async Task RunAsync(EventProjectionFixture fixture, ApplyVectorProjection request,
        Guid failedCommandId)
    {
        await FailedReplayAsync(fixture, request, failedCommandId);
        await RepairAsync(fixture);
        await FailedReplayAsync(fixture, request, failedCommandId);
        var healthyCommandId = Guid.NewGuid();
        var receipt = fixture.Apply(request, healthyCommandId);
        await ClassificationLineageWholeAssertions.ReceiptAsync(fixture, receipt);
        var lineage = fixture.Harness.Store.Read(view => view.ReadOwnedValue(fixture.LineageKey()))
            ?? throw new InvalidOperationException(MissingLineage);
        var vector = fixture.VectorBytes() ?? throw new InvalidOperationException(MissingVector);
        await CompleteCanonicalAsync(fixture);
        await HealthyReadAsync(fixture);
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        await ClassificationLineageWholeAssertions.ReplayReceiptAsync(fixture.Apply(request, healthyCommandId), receipt);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
        var identity = fixture.Harness.Store.Identity;
        fixture.Harness.Reopen();
        await Assert.That(fixture.Harness.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(fixture.Harness.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(fixture.Harness.Store.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
        await FailedReplayAsync(fixture, request, failedCommandId);
        await ClassificationLineageWholeAssertions.ReplayReceiptAsync(fixture.Apply(request, healthyCommandId), receipt);
        await Assert.That(fixture.VectorBytes()!.AsSpan().SequenceEqual(vector)).IsTrue();
        var coldLineage = fixture.Harness.Store.Read(view => view.ReadOwnedValue(fixture.LineageKey()))
            ?? throw new InvalidOperationException(MissingLineage);
        await Assert.That(coldLineage.AsSpan().SequenceEqual(lineage)).IsTrue();
        await CompleteCanonicalAsync(fixture);
        await HealthyReadAsync(fixture);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static async Task FailedReplayAsync(EventProjectionFixture fixture, ApplyVectorProjection request,
        Guid failedCommandId)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        await Assert.That(Refused(fixture, request, failedCommandId).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static async Task RepairAsync(EventProjectionFixture fixture)
    {
        var key = global::KeyLoad.Core.KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId,
            EventProjectionFixture.Collection);
        var current = fixture.Harness.Store.Read(view => view.GetRecord<ResourceDefinition>(key))
            ?? throw new InvalidOperationException(MissingResource);
        await Assert.That(current.SchemaVersion).IsEqualTo(OriginalSchema);
        await Assert.That(current.FieldPolicies.Single(policy => policy.Path == EventProjectionFixture.VectorField)
            .Classification).IsEqualTo(WeakerClass);
        var replacement = current with
        {
            SchemaVersion = RepairedSchema,
            FieldPolicies = [.. current.FieldPolicies.Select(policy => policy.Path == EventProjectionFixture.VectorField
                ? policy with { Classification = EventProjectionFixture.SourceClass } : policy)]
        };
        var actual = fixture.Harness.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, replacement)
            { ExpectedSchemaVersion = OriginalSchema }).Get<ResourceDefinition>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(replacement))).IsTrue();
    }

    private static async Task CompleteCanonicalAsync(EventProjectionFixture fixture)
    {
        var expected = new VectorProjectionLineage(fixture.Stream, OriginalRevision, EventId,
            new(fixture.Partition, EventProjectionFixture.Collection, EventProjectionFixture.SourceId),
            OriginalRevision, EventProjectionFixture.InputField, RepairedSchema, OriginalWorkerEpoch,
            [EventProjectionFixture.SourceClass], fixture.TargetCollection, EventProjectionFixture.TargetId,
            EventProjectionFixture.VectorField, EventProjectionFixture.Space, RepairedSchema, ReducerId,
            ReducerVersion, ReducerGeneration, [EventProjectionFixture.SourceClass]);
        await Assert.That(JsonDefaults.Serialize(fixture.Lineage()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var vector = NativeSerialization.Deserialize<VectorRecord>(fixture.VectorBytes()
            ?? throw new InvalidOperationException(MissingVector));
        var expectedVector = new VectorRecord(EventProjectionFixture.TargetId, EventProjectionFixture.VectorField,
            EventProjectionFixture.Space, [1, 0], OriginalRevision);
        await Assert.That(JsonDefaults.Serialize(vector).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expectedVector))).IsTrue();
    }

    private static async Task HealthyReadAsync(EventProjectionFixture fixture)
    {
        var projection = new NativeTextProjection(Path.Combine(fixture.Harness.DirectoryPath, NativeDirectory),
            UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits), fixture.Harness.Store.Identity.NodeId,
            UnitNativeTextOptions.Execution());
        try
        { await ClassificationLineageWholeAssertions.HealthyAsync(fixture, projection); }
        catch (Exception primary)
        {
            try
            { projection.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        projection.Dispose();
    }
}
