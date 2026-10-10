using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupOracle
{
    internal static string[] Rows(IAtomicStore store, PartitionRef partition, bool includeOutcomes = true)
        => store.Read(view => PartitionRecordFamilies.All.Where(family => includeOutcomes ||
            family is not PartitionRecordFamilies.OutcomeV2 and not PartitionRecordFamilies.OutcomeLocator
                and not PartitionRecordFamilies.OutcomeLocatorV2).SelectMany(family =>
        {
            var page = view.Scan(KeySpace.Partition(family, partition), QueueLifecycleTestProtocol.ImageRecords);
            if (page.HasMore)
            { throw new InvalidOperationException("The mixed retained-state image exceeded its original fixture bound."); }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
        }).ToArray());

    internal static string[] Rows(IAtomicStore store, MixedEventingRetentionBackupState state)
        => state.Partitions.SelectMany(partition => Rows(store, partition)).ToArray();

    internal static async Task InitialAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, DatabaseEngine database)
    {
        await OriginalAsync(fixture, database);
        await MixedEventingRetentionBackupTopic.EqualAsync(state.Outbox, database.GetOutboxStatus(
            EventingArtifactFixture.Principal, fixture.Source.Partition));
        await MixedEventingRetentionBackupTopic.RequireAsync(database, state);
        await QueueLifecycleImage.BodyAsync(database, state.Queue, QueueLifecycleTestProtocol.Parked,
            MessageState.DeadLettered, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.One);
        await QueueLifecycleImage.BodyAsync(database, state.Queue, QueueLifecycleTestProtocol.Pending,
            MessageState.PendingDeadLetter, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.None);
        await QueueLifecycleAccountingAssertions.InitialAsync((ZoneTreeStore)database.Store, state.Queue);
        await TargetInboxNativeAssertions.EffectsAsync(database, state.Inbox);
        await TargetInboxNativeAssertions.CapacityAsync((ZoneTreeStore)database.Store, state.Inbox, TargetInboxUnitProtocol.FirstRevision);
        await RetainedIdentityAsync(database.Store, state, MixedEventingRetentionBackupProtocol.First, 1);
        await RetainedIdentityAsync(database.Store, state, MixedEventingRetentionBackupProtocol.Second, 2);
    }

    private static async Task OriginalAsync(EventingArtifactFixture fixture, DatabaseEngine database)
    {
        var page = database.ReadEventSource(EventingArtifactFixture.Principal, new(fixture.Events));
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(3, 1, 1));
        await Assert.That(page.HasMore).IsFalse();
        await MixedEventingRetentionBackupTopic.EqualAsync(fixture.Received.Deliveries.Select(item => item.Event).ToArray(), page.Events.ToArray());
        await MixedEventingRetentionBackupTopic.EqualAsync(
            new SubscriptionInfo(fixture.Group, new(EventingArtifactFixture.Principal), 1, 1, 1, 3, 3, false, null),
            database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group));
        await MixedEventingRetentionBackupTopic.EqualAsync(
            new SubscriptionInfo(fixture.Other, new(EventingArtifactFixture.Principal), 1, 1, 0, 1, 3, false, null),
            database.GetSubscription(EventingArtifactFixture.Principal, fixture.Other));
        var document = database.GetDocument(EventingArtifactFixture.Principal,
            new(fixture.Source.Partition, EventingArtifactFixture.Documents, EventingArtifactFixture.DocumentId));
        await Assert.That(document!.Reference).IsEqualTo(new EntityRef(fixture.Source.Partition,
            EventingArtifactFixture.Documents, EventingArtifactFixture.DocumentId));
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(EventingArtifactFixture.Json);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        var expected = new MessageInspection(new(EventingArtifactFixture.MessageId, MessageState.Leased, 1, 2, 1,
            null, null, EventingArtifactFixture.Principal, 1, fixture.Time.AddSeconds(30)),
            EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson);
        await Assert.That(JsonSerializer.Serialize(database.InspectMessage(EventingArtifactFixture.Principal,
            fixture.Lane, EventingArtifactFixture.MessageId), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    private static async Task RetainedIdentityAsync(IAtomicStore store, MixedEventingRetentionBackupState state, string id, long position)
    {
        var key = KeySpace.Partition("topic-event-id", state.Topic.Partition, state.Topic.Resource,
            state.Topic.Generation, id, "retained-digest");
        var actual = store.Read(view => view.GetRecord<RetainedTopicEventIdentity>(key));
        await Assert.That(actual).IsEqualTo(new RetainedTopicEventIdentity(
            JsonData.Fingerprint(MixedEventingRetentionBackupTopic.Data(id)), position, state.Topic.Generation));
        var active = KeySpace.Partition("topic-event-id", state.Topic.Partition, state.Topic.Resource, state.Topic.Generation, id);
        await Assert.That(store.Read(view => view.ReadOwnedValue(active))).IsNull();
    }

    internal static async Task SameAsync(string[] expected, IAtomicStore store, MixedEventingRetentionBackupState state)
        => await Assert.That(Rows(store, state)).IsEquivalentTo(expected, CollectionOrdering.Matching);
}
