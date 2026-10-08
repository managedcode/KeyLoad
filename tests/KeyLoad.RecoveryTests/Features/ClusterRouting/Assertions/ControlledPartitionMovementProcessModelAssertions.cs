using System.Text.Json;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Independent full literal model values read through current persisted native authorization.</summary>
internal static class ControlledPartitionMovementProcessModelAssertions
{
    private const long FirstEventPosition = 1;
    private const long FirstEventSequence = 1;
    private const long InitialStateVersion = 1;
    private const long FirstReadySequence = 1;
    private const int EmptyAttempts = 0;
    private const int EventLimit = 1;
    private const string EventType = "knowledge.created";

    internal static async Task ReadAsync(ControlledPartitionMovementNativeNode node, DateTimeOffset recordedAt,
        long expectedCut, CancellationToken cancellationToken)
    {
        await ControlledPartitionMovementProcessVectorAssertions.ReadAsync(node, cancellationToken);
        var partition = ControlledPartitionMovementProcessLiteralCorpus.Partition;
        var reference = new EntityRef(partition, ControlledPartitionMovementProcessLiteralCorpus.Collection,
            ControlledPartitionMovementProcessLiteralCorpus.DocumentId);
        var document = node.Database.GetDocument(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            reference, cancellationToken: cancellationToken);
        await EqualAsync(document, new DocumentResult(reference, ControlledPartitionMovementProcessLiteralCorpus.InitialRevision,
            ControlledPartitionMovementProcessLiteralCorpus.OriginalJson, false, []));
        var lane = new QueueLaneRef(partition, ControlledPartitionMovementProcessLiteralCorpus.Queue);
        var inspection = node.Database.InspectMessage(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            lane, ControlledPartitionMovementProcessLiteralCorpus.MessageId);
        var metadata = new MessageMetadata(ControlledPartitionMovementProcessLiteralCorpus.MessageId, MessageState.Ready,
            EmptyAttempts, InitialStateVersion, FirstReadySequence, null, null);
        await EqualAsync(inspection, new MessageInspection(metadata, ControlledPartitionMovementProcessLiteralCorpus.CanonicalQueueJson,
            ControlledPartitionMovementProcessLiteralCorpus.EmptyHeaders));
        var source = new EventSourceRef(partition, ControlledPartitionMovementProcessLiteralCorpus.Topic, EventSourceKind.Topic);
        var page = node.Database.ReadEventSource(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            new(source, Limit: EventLimit), cancellationToken);
        var expectedHead = new EventSourceHead(FirstEventPosition, FirstEventPosition, source.Generation);
        await EqualAsync(page.Source, source);
        await EqualAsync(page.Head, expectedHead);
        var expected = new SourceEventRecord(source, FirstEventPosition, FirstEventSequence,
            new(ControlledPartitionMovementProcessLiteralCorpus.EventId, EventType, ControlledPartitionMovementProcessLiteralCorpus.EventJson,
                ControlledPartitionMovementProcessLiteralCorpus.EmptyHeaders), recordedAt);
        await Assert.That(page.Events.Length).IsEqualTo(EventLimit);
        await EqualAsync(page.Events.Single(), expected);
        await Assert.That(page.CutPosition).IsEqualTo(expectedCut);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(page.Cursor)).IsFalse();
        var exhausted = node.Database.ReadEventSource(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
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
