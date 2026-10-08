using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Independent full literal model values read through current persisted native authorization.</summary>
internal static class ControlledPartitionMovementModelAssertions
{
    private const long FirstEventPosition = 1;
    private const long FirstEventSequence = 1;
    private const long InitialStateVersion = 1;
    private const long FirstReadySequence = 1;
    private const int EmptyAttempts = 0;
    private const int EventLimit = 1;
    private const string EventType = "knowledge.created";

    internal static async Task ReadAsync(ControlledPartitionMovementNode node, DateTimeOffset recordedAt,
        long expectedCut, CancellationToken cancellationToken)
    {
        await ControlledPartitionMovementVectorAssertions.ReadAsync(node, cancellationToken);
        var partition = ControlledPartitionMovementCorpus.Partition;
        var reference = new EntityRef(partition, ControlledPartitionMovementCorpus.Collection,
            ControlledPartitionMovementCorpus.DocumentId);
        var document = node.Database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, cancellationToken: cancellationToken);
        await EqualAsync(document, new DocumentResult(reference, ControlledPartitionMovementCorpus.InitialRevision,
            ControlledPartitionMovementCorpus.OriginalJson, false, []));
        var lane = new QueueLaneRef(partition, ControlledPartitionMovementCorpus.Queue);
        var inspection = node.Database.InspectMessage(PhysicalShardCatalogFixture.RootPrincipalId,
            lane, ControlledPartitionMovementCorpus.MessageId);
        var metadata = new MessageMetadata(ControlledPartitionMovementCorpus.MessageId, MessageState.Ready,
            EmptyAttempts, InitialStateVersion, FirstReadySequence, null, null);
        await EqualAsync(inspection, new MessageInspection(metadata, ControlledPartitionMovementCorpus.CanonicalQueueJson,
            ControlledPartitionMovementCorpus.EmptyHeaders));
        var source = new EventSourceRef(partition, ControlledPartitionMovementCorpus.Topic, EventSourceKind.Topic);
        var page = node.Database.ReadEventSource(PhysicalShardCatalogFixture.RootPrincipalId,
            new(source, Limit: EventLimit), cancellationToken);
        var expectedHead = new EventSourceHead(FirstEventPosition, FirstEventPosition, source.Generation);
        await EqualAsync(page.Source, source);
        await EqualAsync(page.Head, expectedHead);
        var expected = new SourceEventRecord(source, FirstEventPosition, FirstEventSequence,
            new(ControlledPartitionMovementCorpus.EventId, EventType, ControlledPartitionMovementCorpus.EventJson,
                ControlledPartitionMovementCorpus.EmptyHeaders), recordedAt);
        await Assert.That(page.Events.Length).IsEqualTo(EventLimit);
        await EqualAsync(page.Events.Single(), expected);
        await Assert.That(page.CutPosition).IsEqualTo(expectedCut);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(page.Cursor)).IsFalse();
        var exhausted = node.Database.ReadEventSource(PhysicalShardCatalogFixture.RootPrincipalId,
            new(source, Limit: EventLimit, Cursor: page.Cursor), cancellationToken);
        await EqualAsync(exhausted.Source, source);
        await EqualAsync(exhausted.Head, expectedHead);
        await Assert.That(exhausted.Events).IsEmpty();
        await Assert.That(exhausted.HasMore).IsFalse();
        await Assert.That(exhausted.CutPosition).IsEqualTo(expectedCut);
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
}
