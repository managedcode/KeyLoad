using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reads every independently literal original mixed-model value from the actual production owner.</summary>
internal static class ControlledPartitionMovementNativeModels
{
    private const long FirstPosition = 1;
    private const int EmptyAttempts = 0;
    private const int EventLimit = 1;
    private const double SingleRank = 1.0 / 61.0;

    internal static async Task ReadAsync(ControlledPartitionMovementNativeHost owner,
        DateTimeOffset recordedAt, CancellationToken cancellationToken)
    {
        await VectorAsync(owner, cancellationToken);
        var database = owner.Database;
        var partition = ControlledPartitionMovementCorpus.Partition;
        var reference = new EntityRef(partition, ControlledPartitionMovementCorpus.Collection,
            ControlledPartitionMovementCorpus.DocumentId);
        var document = database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, cancellationToken: cancellationToken);
        await EqualAsync(document, new DocumentResult(reference, FirstPosition,
            ControlledPartitionMovementCorpus.OriginalJson, false, []));
        var inspection = database.InspectMessage(PhysicalShardCatalogFixture.RootPrincipalId,
            new(partition, ControlledPartitionMovementCorpus.Queue), ControlledPartitionMovementCorpus.MessageId);
        var metadata = new MessageMetadata(ControlledPartitionMovementCorpus.MessageId, MessageState.Ready,
            EmptyAttempts, FirstPosition, FirstPosition, null, null);
        await EqualAsync(inspection, new MessageInspection(metadata, ControlledPartitionMovementCorpus.CanonicalQueueJson,
            ControlledPartitionMovementCorpus.EmptyHeaders));
        var source = new EventSourceRef(partition, ControlledPartitionMovementCorpus.Topic, EventSourceKind.Topic);
        var cut = owner.Store.Position;
        var page = database.ReadEventSource(PhysicalShardCatalogFixture.RootPrincipalId,
            new(source, Limit: EventLimit), cancellationToken);
        await EqualAsync(page.Source, source);
        await EqualAsync(page.Head, new EventSourceHead(FirstPosition, FirstPosition, source.Generation));
        await Assert.That(page.Events.Length).IsEqualTo(EventLimit);
        await EqualAsync(page.Events.Single(), new SourceEventRecord(source, FirstPosition, FirstPosition,
            new(ControlledPartitionMovementCorpus.EventId, "knowledge.created", ControlledPartitionMovementCorpus.EventJson,
                ControlledPartitionMovementCorpus.EmptyHeaders), recordedAt));
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(page.Cursor)).IsFalse();
        var exhausted = database.ReadEventSource(PhysicalShardCatalogFixture.RootPrincipalId,
            new(source, Limit: EventLimit, Cursor: page.Cursor), cancellationToken);
        await EqualAsync(exhausted.Source, source);
        await EqualAsync(exhausted.Head, new EventSourceHead(FirstPosition, FirstPosition, source.Generation));
        await Assert.That(exhausted.Events).IsEmpty();
        await Assert.That(exhausted.HasMore).IsFalse();
        await Assert.That(exhausted.CutPosition).IsEqualTo(cut);
    }

    private static async Task VectorAsync(ControlledPartitionMovementNativeHost owner,
        CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var expected = new VectorRecord(ControlledPartitionMovementCorpus.DocumentId,
            ControlledPartitionMovementCorpus.VectorField, ControlledPartitionMovementCorpus.Space, [1f, 0f], FirstPosition);
        var vector = owner.Store.Read(view => view.GetRecord<VectorRecord>(KeySpace.Partition("vector", partition,
            ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.VectorField,
            ControlledPartitionMovementCorpus.DocumentId)));
        await EqualAsync(vector, expected);
        var rows = await new global::KeyLoad.Query.SearchEngine(owner.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(
            PhysicalShardCatalogFixture.RootPrincipalId, new(partition, ControlledPartitionMovementCorpus.Collection,
                VectorField: ControlledPartitionMovementCorpus.VectorField, Vector: [1f, 0f],
                Space: ControlledPartitionMovementCorpus.Space), cancellationToken);
        var document = new DocumentResult(new(partition, ControlledPartitionMovementCorpus.Collection,
            ControlledPartitionMovementCorpus.DocumentId), FirstPosition,
            ControlledPartitionMovementCorpus.OriginalJson, false, []);
        await EqualAsync(rows.ToArray(), new[] { new RankedDocument(document, SingleRank) });
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
}
