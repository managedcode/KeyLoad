using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupTopic
{
    private const long OriginalApplyStep = 1;
    internal static async Task SeedAsync(TestDatabase owner, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        owner.Configure(MixedEventingRetentionBackupProtocol.Topic, ResourceKind.Topic);
        state.Publication = new(Guid.NewGuid(), owner.Partition,
            [new PublishTopic(state.Topic.Resource, [Data(MixedEventingRetentionBackupProtocol.First),
                Data(MixedEventingRetentionBackupProtocol.Second), Data(MixedEventingRetentionBackupProtocol.Third)])]);
        state.PublicationAt = owner.Database.EvaluationClock.GetUtcNow();
        state.PublicationReceipt = owner.Submit(OperationKind.Batch, state.Publication,
            id: state.Publication.CommandId, time: state.PublicationAt).Get<CommitReceipt>();
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), state.Pin, new(EventingArtifactFixture.Principal));
        _ = owner.Submit(OperationKind.ConfigureSubscription, configure, id: configure.CommandId).Get<SubscriptionInfo>();
        var refused = new CommandRequest(Guid.NewGuid(), owner.Partition,
            [new PurgeTopic(state.Topic.Resource, MixedEventingRetentionBackupProtocol.PurgePosition)]);
        var before = MixedEventingRetentionBackupOracle.Rows(owner.Store, owner.Partition, includeOutcomes: false);
        var result = owner.Submit(OperationKind.Batch, refused, id: refused.CommandId);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(MixedEventingRetentionBackupOracle.Rows(owner.Store, owner.Partition, includeOutcomes: false))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), state.Pin, 1, SubscriptionStart.FromNow);
        var sought = owner.Submit(OperationKind.SeekSubscription, seek, id: seek.CommandId).Get<SubscriptionInfo>();
        await Assert.That(sought.Checkpoint).IsEqualTo(MixedEventingRetentionBackupProtocol.LastPosition);
        await Assert.That(sought.Paused).IsTrue();
        state.Purge = refused with { CommandId = Guid.NewGuid() };
        var physicalBefore = owner.Store.Position;
        var journalBefore = owner.ReadJournalCut();
        state.PurgeReceipt = owner.Submit(OperationKind.Batch, state.Purge, id: state.Purge.CommandId).Get<CommitReceipt>();
        await Assert.That(state.PurgeReceipt.CommandId).IsEqualTo(state.Purge.CommandId);
        await EqualAsync(new[] { new MutationReceipt("purgeTopic", state.Topic.Resource, "2", 2) },
            state.PurgeReceipt.Mutations.ToArray());
        await Assert.That(state.PurgeReceipt.Token.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        var originalPurge = owner.ReadRetainedEntry(state.Purge.CommandId);
        await Assert.That(originalPurge.Index).IsEqualTo(checked(journalBefore.CommittedIndex + OriginalApplyStep));
        await Assert.That(state.PurgeReceipt.Token.Position).IsEqualTo(originalPurge.Index);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(originalPurge.Index);
        await Assert.That(owner.Store.Position).IsEqualTo(checked(physicalBefore + OriginalApplyStep));
        await RequireAsync(owner.Database, state);
        var rows = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        await EqualAsync(state.PurgeReceipt, owner.Submit(OperationKind.Batch, state.Purge, id: state.Purge.CommandId).Get<CommitReceipt>());
        await EqualAsync(state.PublicationReceipt, owner.Submit(OperationKind.Batch, state.Publication,
            id: state.Publication.CommandId).Get<CommitReceipt>());
        await Assert.That(owner.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store)).IsEquivalentTo(rows, CollectionOrdering.Matching);
        token.ThrowIfCancellationRequested();
    }

    internal static EventData Data(string id) => new(id, MixedEventingRetentionBackupProtocol.EventType,
        MixedEventingRetentionBackupProtocol.Payload, MixedEventingRetentionBackupProtocol.Headers);

    internal static async Task RequireAsync(DatabaseEngine database, MixedEventingRetentionBackupState state)
    {
        var page = database.ReadEventSource(EventingArtifactFixture.Principal,
            new(state.Topic, AfterPosition: MixedEventingRetentionBackupProtocol.PurgePosition));
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(3, 3, 1));
        await Assert.That(page.HasMore).IsFalse();
        var expected = new SourceEventRecord(state.Topic, MixedEventingRetentionBackupProtocol.LastPosition,
            MixedEventingRetentionBackupProtocol.LastSequence, Data(MixedEventingRetentionBackupProtocol.Third), state.PublicationAt);
        await Assert.That(JsonSerializer.Serialize(page.Events, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(new[] { expected }, JsonDefaults.Options));
        var unavailable = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadEventSource(EventingArtifactFixture.Principal, new(state.Topic)));
        await Assert.That(unavailable.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        var pin = database.GetSubscription(EventingArtifactFixture.Principal, state.Pin);
        await EqualAsync(new SubscriptionInfo(state.Pin, new(EventingArtifactFixture.Principal), 2, 2, 3, 3, 3, true, null), pin);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
}
