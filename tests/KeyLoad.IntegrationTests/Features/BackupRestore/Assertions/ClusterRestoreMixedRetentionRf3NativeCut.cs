using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3NativeCut
{
    internal static async Task SourceAsync(ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ImmutableArray<string> archives, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var source = originals.Single(receipt => receipt.Cut.Owner.PhysicalShardId == state.SourceOwner.PhysicalShardId);
        var target = originals.Single(receipt => receipt.Cut.Owner.PhysicalShardId == state.TargetOwner.PhysicalShardId);
        await Assert.That(source.Cut.Owner.PhysicalShardId).IsNotEqualTo(target.Cut.Owner.PhysicalShardId);
        await PlacementAsync(source, state.Source.Partition, state.SourceOwner);
        await PlacementAsync(target, state.Partition, state.TargetOwner);
        var archive = archives[originals.IndexOf(target)];
        var outcomes = ZoneTreeStore.ReadVerifiedCatalogBackup(archive, IntegrationExecutionOptions.StorageExecution(),
            (view, identity, position, metadata) =>
            {
                if (identity.NodeId != target.Cut.SourceNodeId || identity.Incarnation != target.Cut.Owner.Incarnation
                    || position != target.Cut.StorePosition
                    || !NativeSerialization.Serialize(target.Cut).AsSpan().SequenceEqual(metadata.Span))
                { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
                return OriginalOutcomes(view, state, token);
            }, token);
        foreach (var (id, bytes) in outcomes)
        { state.OriginalOutcomeBytes.Add(id, bytes); }
    }

    internal static void RequireRetained(ClusterRestoreRf3Fixture target, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        const int FirstIndex = 0;
        const int ReplicaCount = 3;
        var ownerIndex = target.Mappings.ToList().FindIndex(mapping => mapping.Source.PhysicalShardId == state.TargetOwner.PhysicalShardId);
        if (ownerIndex < FirstIndex)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var failures = new List<Exception>();
        foreach (var node in ClusterRestoreRf3Protocol.Nodes.Skip(ownerIndex * ReplicaCount).Take(ReplicaCount))
        {
            ZoneTreeStore? store = null;
            ServerFailureObserver.Observe(() =>
            {
                token.ThrowIfCancellationRequested();
                store = new(new(Path.Combine(target.DataRoot, node, ClusterRestoreRf3Protocol.DatabaseDirectory)),
                    IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
                store.Read(view => RetainedView(view, state));
            }, failures);
            if (store is { } owned)
            { ServerFailureObserver.Observe(owned.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static bool RetainedView(IKeyValueView view, ClusterRestoreMixedRetentionRf3State state)
    {
        foreach (var (id, original) in state.OriginalOutcomeBytes)
        {
            var retained = view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition, state.Principal.Id, id));
            if (retained is null || !original.AsSpan().SequenceEqual(retained))
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        }
        return true;
    }

    private static async Task PlacementAsync(ClusterBackupOwnerReceipt receipt, PartitionRef partition, PhysicalShardRecord owner)
    {
        var cut = receipt.Cut.Partitions.Single(item => item.Roster.Partition == partition);
        await Assert.That(cut.CanonicalRecordCount).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
        await Assert.That(cut.CanonicalDigest).IsNotEmpty();
        await Assert.That(cut.RosterDigest).IsNotEmpty();
        await Assert.That(cut.AppliedIndex).IsGreaterThan(ClusterRestoreMixedRetentionRf3Protocol.NoRevision);
        await Assert.That(cut.Placement.PhysicalShardId).IsEqualTo(owner.PhysicalShardId);
        await Assert.That(cut.Placement.Incarnation).IsEqualTo(owner.Incarnation);
    }

    private static Dictionary<Guid, byte[]> OriginalOutcomes(IKeyValueView view, ClusterRestoreMixedRetentionRf3State state, CancellationToken token)
    {
        var commands = state.OriginalQueue.Commands.Select(original => original.Command.CommandId)
            .Append(state.Inbox.CommandId).Append(state.Purge.CommandId).Append(state.FilteredPublish.CommandId)
            .Append(state.PurgedPublish.CommandId).Distinct().ToArray();
        var results = new Dictionary<Guid, byte[]>();
        var remaining = IntegrationExecutionOptions.DatabaseLimits().Value.MaxBatchBytes;
        foreach (var id in commands)
        {
            token.ThrowIfCancellationRequested();
            var key = KeySpace.PartitionOutcome(state.Partition, state.Principal.Id, id);
            var bytes = view.ReadOwnedValue(key) ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
            if (bytes.Length > remaining)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            remaining -= bytes.Length;
            var outcome = NativeSerialization.Deserialize<StoredOutcome>(bytes);
            if (outcome.Partition != state.Partition || outcome.ScopeKind != CommandOutcomeScopeKind.Partition
                || outcome.PolicyEpoch != ClusterRestoreMixedRetentionRf3Protocol.InitialEpoch || outcome.Result.Error is not null)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            RequireOutcomeResult(outcome, id, state);
            results.Add(id, bytes);
        }
        InboxAndRetention(view, state);
        var sizes = new[] { QueueLifecyclePublicProtocol.Parked, QueueLifecyclePublicProtocol.Pending }.Select(id =>
        {
            var enqueue = new EnqueueMessage(state.OriginalQueue.Lane.Queue, id, QueueLifecyclePublicProtocol.Payload,
                QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering);
            return NativeSerialization.Serialize(new MessageBody(id, JsonData.Validate(enqueue.PayloadJson, IntegrationExecutionOptions.DatabaseLimits().Value),
                JsonData.Validate(enqueue.HeadersJson, IntegrationExecutionOptions.DatabaseLimits().Value),
                enqueue.OrderingKey, JsonData.Fingerprint(enqueue))).LongLength;
        }).ToArray();
        var counters = view.GetRecord<QueueCounters>(KeySpace.Partition(ClusterRestoreMixedRetentionRf3Protocol.QueueCounterSpace,
            state.Partition, state.OriginalQueue.Lane.Queue));
        if (counters != new QueueCounters(QueueLifecyclePublicProtocol.Two, sizes.Sum(),
            ClusterRestoreMixedRetentionRf3Protocol.NoRevision, ClusterRestoreMixedRetentionRf3Protocol.NoRevision,
            QueueLifecyclePublicProtocol.Two, QueueLifecyclePublicProtocol.One, sizes[ClusterRestoreMixedRetentionRf3Protocol.FirstIndex],
            QueueLifecyclePublicProtocol.One, ClusterRestoreMixedRetentionRf3Protocol.NoRevision))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        return results;
    }
    private static void RequireOutcomeResult(StoredOutcome outcome, Guid id, ClusterRestoreMixedRetentionRf3State state)
    {
        if (id == state.Inbox.CommandId)
        {
            if (!NativeSerialization.Serialize(outcome.Result.Get<CommitInboxResult>()).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(state.InboxResult)))
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            return;
        }
        var expected = state.OriginalQueue.Commands.SingleOrDefault(original => original.Command.CommandId == id).Receipt
            ?? (id == state.Purge.CommandId ? state.PurgeReceipt
                : id == state.FilteredPublish.CommandId ? state.FilteredPublishReceipt : state.PurgedPublishReceipt);
        if (!NativeSerialization.Serialize(outcome.Result.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected)))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
    }

    private static void InboxAndRetention(IKeyValueView view, ClusterRestoreMixedRetentionRf3State state)
    {
        var record = view.GetRecord<TargetInboxRecord>(TargetInboxStorage.Key(state.Inbox))
            ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        var expected = new TargetInboxRecord(TargetInboxStorage.Identity(state.Inbox), JsonData.Fingerprint(state.Inbox.Effects), state.InboxResult.Receipt);
        if (!NativeSerialization.Serialize(record).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var capacity = TargetInboxStorage.Capacity(view, state.Target, required: true);
        var keyBytes = TargetInboxStorage.Key(state.Inbox).LongLength;
        var recordBytes = NativeSerialization.Serialize(expected).LongLength;
        if (capacity != new TargetInboxCapacity(ClusterRestoreMixedRetentionRf3Protocol.FirstRevision, checked(keyBytes + recordBytes)))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        foreach (var (data, index) in ClusterRestoreMixedRetentionRf3PurgeTopic.Events()
            .Take((int)ClusterRestoreMixedRetentionRf3Protocol.PurgePosition).Select((data, index) => (data, index)))
        {
            var key = KeySpace.Partition(ClusterRestoreMixedRetentionRf3Protocol.TopicIdentitySpace, state.Partition,
                state.PurgedSource.Resource, state.PurgedSource.Generation, data.EventId);
            var retained = view.GetRecord<RetainedTopicEventIdentity>(KeySpace.Partition(
                ClusterRestoreMixedRetentionRf3Protocol.TopicIdentitySpace, state.Partition, state.PurgedSource.Resource,
                state.PurgedSource.Generation, data.EventId, ClusterRestoreMixedRetentionRf3Protocol.RetainedDigest));
            if (view.ReadOwnedValue(key) is not null || retained != new RetainedTopicEventIdentity(JsonData.Fingerprint(data),
                index + ClusterRestoreMixedRetentionRf3Protocol.FirstRevision, state.PurgedSource.Generation))
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        }
    }

}
