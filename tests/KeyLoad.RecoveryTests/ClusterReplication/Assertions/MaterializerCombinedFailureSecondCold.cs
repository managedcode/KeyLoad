using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.Messaging;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.RecoveryTests.MaterializerCombinedFailureProtocol;

namespace KeyLoad.RecoveryTests;

internal static class MaterializerCombinedFailureSecondCold
{
    internal static async Task RunAsync(MaterializerCombinedFailureFixture fixture, byte[] identity,
        string[] image, string[] replicaImage, long position, long replicaPosition, ReplicaEntry configured,
        (ReplicaEntry Entry, CommitReceipt Receipt) healthy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var canonical = new ZoneTreeStore(new(Path.Combine(fixture.Directory, FaultCanonicalDirectory))
        {
            Incarnation = fixture.Configuration.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        using var replica = new ZoneTreeStore(new(fixture.Configuration.Directory)
        {
            Incarnation = fixture.Configuration.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var db = MaterializerCombinedFailureFixture.OpenDatabase(canonical);
        using var log = new DurableReplicaLog(replica, RecoveryExecutionOptions.Configuration(fixture.Configuration), canonicalDatabase: db);
        await using var materializer = new ReplicaMaterializer(db, log, new ReplicaSnapshotStore(canonical, log,
            RecoveryExecutionOptions.Configuration(fixture.Configuration), RecoveryExecutionOptions.Replica(), replacementToken => db.RequireNoRetirementForSnapshotReplacement(
                    new(db.OperationLimitsOptions, db.EvaluationClock, replacementToken))), RecoveryExecutionOptions.Replica());
        await materializer.WaitForApplyAsync(HealthyEnqueueIndex, token);
        await RecordedQueueFollowerAssertions.EqualAsync(db.LastApplied, HealthyEnqueueIndex);
        await RecordedQueueFollowerAssertions.EqualAsync(canonical.Position, position);
        await RecordedQueueFollowerAssertions.EqualAsync(replica.Position, replicaPosition);
        await RecordedQueueFollowerAssertions.EqualAsync(NativeSerialization.Serialize(canonical.Identity), identity);
        await Assert.That(RecordedQueueFollowerNative.Image(canonical)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(RecordedQueueFollowerNative.Image(replica)).IsEquivalentTo(replicaImage, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await RecordedQueueFollowerAssertions.EqualAsync(log.ReadEntry(configured.Index), configured);
        await RecordedQueueFollowerAssertions.EqualAsync(log.ReadEntry(healthy.Entry.Index), healthy.Entry);
        await RecordedQueueFollowerAssertions.EqualAsync(OutcomeStoreOracle.Read(canonical, healthy.Entry.Operation!)!.Get<CommitReceipt>(), healthy.Receipt);
        await RecordedQueueFollowerAssertions.EqualAsync(OutcomeStoreOracle.Read(canonical, configured.Operation!)!.Get<ResourceDefinition>(),
            new ResourceDefinition(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId));
        await HealthyImageAsync(db, new(Queue, Message, Payload, Headers));
    }

    internal static async Task HealthyImageAsync(DatabaseEngine db, EnqueueMessage message)
    {
        var lane = new QueueLaneRef(Partition, Queue);
        var body = new MessageBody(Message, Payload, Headers, message.OrderingKey, JsonData.Fingerprint(message));
        var metadata = new MessageMetadata(Message, MessageState.Ready, NoAttempts, InitialStateVersion,
            FirstReadySequence, null, null);
        var inspected = db.InspectMessage(Principal, lane, Message);
        await Assert.That(inspected).IsNotNull();
        await RecordedQueueFollowerAssertions.EqualAsync(inspected!.Metadata, metadata);
        await Assert.That(inspected.PayloadJson).IsEqualTo(Payload);
        await Assert.That(inspected.HeadersJson).IsEqualTo(Headers);
        await RecordedQueueFollowerAssertions.EqualAsync(db.Store.Read(view => view.GetRecord<MessageBody>(
            KeySpace.Partition(RecordedQueueFollowerProtocol.BodyFamily, Partition, Queue, Message))), body);
        await RecordedQueueFollowerAssertions.EqualAsync(db.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition(RecordedQueueFollowerProtocol.CounterFamily, Partition, Queue))),
            new QueueCounters(SingleMessageCount, NativeSerialization.Serialize(body).LongLength, NoQueueUsage, NoQueueUsage, FirstReadySequence));
        foreach (var family in new[] { RecordedQueueFollowerProtocol.MetadataFamily, RecordedQueueFollowerProtocol.BodyFamily,
            RecordedQueueFollowerProtocol.CounterFamily, RecordedQueueFollowerProtocol.ReadyFamily,
            RecordedQueueFollowerProtocol.ScheduledFamily, RecordedQueueFollowerProtocol.LeasedFamily,
            RecordedQueueFollowerProtocol.PendingFamily, RecordedQueueFollowerProtocol.ParkedFamily,
            RecordedQueueFollowerProtocol.DeadLetterFamily, RecordedQueueFollowerProtocol.QueueOrderFamily,
            RecordedQueueFollowerProtocol.InboxFamily })
        {
            var page = db.Store.Read(view => view.Scan(KeySpace.Partition(family, Partition, Queue), RecordedQueueFollowerProtocol.ImageRecords));
            await Assert.That(page.HasMore).IsFalse();
            var expected = family switch
            {
                RecordedQueueFollowerProtocol.MetadataFamily => Row(KeySpace.Partition(family, Partition, Queue, Message), metadata),
                RecordedQueueFollowerProtocol.BodyFamily => Row(KeySpace.Partition(family, Partition, Queue, Message), body),
                RecordedQueueFollowerProtocol.CounterFamily => Row(KeySpace.Partition(family, Partition, Queue),
                    new QueueCounters(SingleMessageCount, NativeSerialization.Serialize(body).LongLength, NoQueueUsage, NoQueueUsage, FirstReadySequence)),
                RecordedQueueFollowerProtocol.ReadyFamily => Row(KeySpace.Partition(family, Partition, Queue, FirstReadySequence, Message), Message),
                _ => []
            };
            await Assert.That(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)))
                .IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    private static string[] Row<T>(byte[] key, T value)
        => [Convert.ToHexString(key) + ":" + Convert.ToHexString(NativeSerialization.Serialize(value))];
}
