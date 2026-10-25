using KeyLoad.Core.Search;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class ClassificationLineageWholeAssertions
{
    private const string Query = "derived";
    private const string EmptyJson = "{}";
    private const double RankContribution = 1d / 61d;
    private const long DocumentRevision = 1;
    private const int ResultCount = 1;
    private const long InitialSchemaVersion = 1;
    private const long OriginalWorkerPolicyEpoch = 1;
    private const long ReducerGeneration = 1;
    private const string SourceEventId = "projection-event";
    private const string ReducerId = "projection-reducer";
    private const string ReducerVersion = "v1";
    private const string ProjectionReceiptKind = "applyVectorProjection";

    internal static async Task ReceiptAsync(EventProjectionFixture fixture, MutationReceipt receipt)
    {
        await Assert.That(receipt.Kind).IsEqualTo(ProjectionReceiptKind);
        await Assert.That(receipt.Resource).IsEqualTo(fixture.TargetCollection);
        await Assert.That(receipt.Id).IsEqualTo(EventProjectionFixture.TargetId);
        await Assert.That(receipt.Revision).IsEqualTo(DocumentRevision);
        await Assert.That(receipt.CompositionReferences).IsEmpty();
    }

    internal static async Task ReplayReceiptAsync(MutationReceipt actual, MutationReceipt original)
    {
        await Assert.That(actual.Kind).IsEqualTo(original.Kind);
        await Assert.That(actual.Resource).IsEqualTo(original.Resource);
        await Assert.That(actual.Id).IsEqualTo(original.Id);
        await Assert.That(actual.Revision).IsEqualTo(original.Revision);
        await Assert.That(actual.CompositionReferences).IsEquivalentTo(original.CompositionReferences,
            CollectionOrdering.Matching);
    }

    internal static async Task HealthyAsync(EventProjectionFixture fixture, RuntimeTextProjection projection)
    {
        var before = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        await LiteralAsync(fixture, await fixture.SearchAsync());
        await LiteralAsync(fixture, await TextAsync(fixture, projection));
        await UnchangedAsync(fixture, before, cut);
    }

    internal static async Task RefusedAsync(EventProjectionFixture fixture, RuntimeTextProjection projection)
    {
        var before = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        var vector = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.SearchAsync)
            ?? throw new InvalidOperationException("The revoked vector reader was accepted.");
        var text = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => TextAsync(fixture, projection))
            ?? throw new InvalidOperationException("The revoked execution text reader was accepted.");
        await Assert.That(vector.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(text.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await UnchangedAsync(fixture, before, cut);
    }

    internal static async Task UnchangedAsync(EventProjectionFixture fixture, string[] before, long cut)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Harness.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(fixture.Harness.Store.Position).IsEqualTo(cut);
    }

    internal static async Task RetainedAsync(EventProjectionFixture fixture, byte[] vector, byte[] lineage)
    {
        await Assert.That(fixture.VectorBytes()).IsEquivalentTo(vector, CollectionOrdering.Matching);
        var actual = fixture.Harness.Store.Read(view => view.ReadOwnedValue(fixture.LineageKey()));
        await Assert.That(actual).IsEquivalentTo(lineage, CollectionOrdering.Matching);
        var record = fixture.Lineage() ?? throw new InvalidOperationException("The retained execution lineage is missing.");
        await LineageAsync(fixture, record);
        var expected = new VectorRecord(EventProjectionFixture.TargetId, EventProjectionFixture.VectorField,
            EventProjectionFixture.Space, [1, 0], DocumentRevision);
        var actualVector = OrleansSerialization.Deserialize<VectorRecord>(vector);
        await Assert.That(actualVector.DocumentId).IsEqualTo(expected.DocumentId);
        await Assert.That(actualVector.Field).IsEqualTo(expected.Field);
        await Assert.That(actualVector.Space).IsEqualTo(expected.Space);
        await Assert.That(actualVector.DocumentRevision).IsEqualTo(expected.DocumentRevision);
        await Assert.That(actualVector.Values).IsEquivalentTo(expected.Values, CollectionOrdering.Matching);
    }

    private static async Task LineageAsync(EventProjectionFixture fixture, VectorProjectionLineage record)
    {
        await Assert.That(record.SourceStream).IsEqualTo(fixture.Stream);
        await Assert.That(record.SourceEventRevision).IsEqualTo(DocumentRevision);
        await Assert.That(record.SourceEventId).IsEqualTo(SourceEventId);
        await Assert.That(record.SourceDocument).IsEqualTo(fixture.Harness.ExpectedReference(new(fixture.Partition,
            EventProjectionFixture.Collection, EventProjectionFixture.SourceId)));
        await Assert.That(record.SourceDocumentRevision).IsEqualTo(DocumentRevision);
        await Assert.That(record.InputField).IsEqualTo(EventProjectionFixture.InputField);
        await Assert.That(record.SourceSchemaVersion).IsEqualTo(InitialSchemaVersion);
        await Assert.That(record.SourcePolicyEpoch).IsEqualTo(OriginalWorkerPolicyEpoch);
        await Assert.That(record.TargetCollection).IsEqualTo(fixture.TargetCollection);
        await Assert.That(record.TargetId).IsEqualTo(EventProjectionFixture.TargetId);
        await Assert.That(record.TargetField).IsEqualTo(EventProjectionFixture.VectorField);
        await Assert.That(record.TargetSpace).IsEqualTo(EventProjectionFixture.Space);
        await Assert.That(record.TargetSchemaVersion).IsEqualTo(InitialSchemaVersion);
        await Assert.That(record.ReducerId).IsEqualTo(ReducerId);
        await Assert.That(record.ReducerVersion).IsEqualTo(ReducerVersion);
        await Assert.That(record.ReducerGeneration).IsEqualTo(ReducerGeneration);
        await Assert.That(record.SourceClassifications).IsEquivalentTo(new[] { EventProjectionFixture.SourceClass }, CollectionOrdering.Matching);
        await Assert.That(record.TargetClassifications).IsEquivalentTo(new[] { EventProjectionFixture.SourceClass }, CollectionOrdering.Matching);
    }

    private static Task<RankedDocument[]> TextAsync(EventProjectionFixture fixture, RuntimeTextProjection projection)
        => new SearchEngine(fixture.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync(
            EventProjectionFixture.ReaderId, new(fixture.Partition, fixture.TargetCollection,
                EventProjectionFixture.InputField, Query), TestContext.Current!.Execution.CancellationToken);

    private static async Task LiteralAsync(EventProjectionFixture fixture, RankedDocument[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(ResultCount);
        var row = actual.Single();
        await Assert.That(row.Document.Reference).IsEqualTo(fixture.Harness.ExpectedReference(new(fixture.Partition,
            fixture.TargetCollection, EventProjectionFixture.TargetId)));
        await Assert.That(row.Document.Revision).IsEqualTo(DocumentRevision);
        await Assert.That(row.Document.Json).IsEqualTo(EmptyJson);
        await Assert.That(row.Document.Redacted).IsTrue();
        await Assert.That(row.Document.RedactedFields).IsEquivalentTo(new[]
            { EventProjectionFixture.InputField, EventProjectionFixture.VectorField }, CollectionOrdering.Matching);
        await Assert.That(row.Score).IsEqualTo(RankContribution);
        await Assert.That(row.Explanation).IsNull();
    }
}
