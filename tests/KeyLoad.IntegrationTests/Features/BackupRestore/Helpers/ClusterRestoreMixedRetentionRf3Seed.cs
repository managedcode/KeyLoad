using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreMixedRetentionRf3Seed
{
    internal static Task<ClusterRestoreMixedRetentionRf3State> RunAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed original, int route, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(source.Application, TwoRf3MembershipProtocol.Node1, source.Profile.AdminKey,
            async root =>
            {
                var state = await ConfigureAsync(root.Sdk, original, route, token).ConfigureAwait(false);
                await RequestCqrsRf3Callers.RunOwnedAsync(source.Application, TwoRf3MembershipProtocol.Node4, state.Key,
                    async callers =>
                    {
                        await QueueLifecyclePublicPhase.BeginAsync(callers, state.OriginalQueue, token).ConfigureAwait(false);
                        await ClusterRestoreMixedRetentionRf3FilteredTopic.SeedAsync(callers, state, token).ConfigureAwait(false);
                        await ClusterRestoreMixedRetentionRf3PurgeTopic.SeedAsync(callers, state, token).ConfigureAwait(false);
                        await ClusterRestoreMixedRetentionRf3Inbox.SeedAsync(callers, state, token).ConfigureAwait(false);
                        await ClusterRestoreMixedRetentionRf3Literal.OriginalAsync(callers, state, token).ConfigureAwait(false);
                        return true;
                    }, token).ConfigureAwait(false);
                return state;
            }, token);

    private static async Task<ClusterRestoreMixedRetentionRf3State> ConfigureAsync(KeyLoadClient root,
        PartitionMovementPublicParentRf3Seed original, int route, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(ClusterRestoreMixedRetentionRf3Protocol.TenantPrefix + suffix,
            ClusterRestoreMixedRetentionRf3Protocol.Database, ClusterRestoreMixedRetentionRf3Protocol.Domain,
            ClusterRestoreMixedRetentionRf3Protocol.TargetKey);
        var target = await BindAsync(root, original, partition, token).ConfigureAwait(false);
        var id = ClusterRestoreMixedRetentionRf3Protocol.OperatorPrefix + suffix;
        var inspectorId = ClusterRestoreMixedRetentionRf3Protocol.InspectorPrefix + suffix;
        var principal = Principal(partition, id);
        var inspector = new PrincipalRecord(inspectorId, partition.TenantId,
            [new(partition.DatabaseId, QueueLifecyclePublicProtocol.Queue,
                Capability.QueueInspect | Capability.DeadLettersRead | Capability.Query)], []);
        await ResourcesAsync(root, partition, token).ConfigureAwait(false);
        await CredentialAsync(root, principal, token).ConfigureAwait(false);
        await CredentialAsync(root, inspector, token).ConfigureAwait(false);
        return new(new(partition, id + ClusterRestoreMixedRetentionRf3Protocol.CredentialSuffix,
            inspectorId + ClusterRestoreMixedRetentionRf3Protocol.CredentialSuffix, route), principal, inspector,
            original.Directory.ControlOwner, target);
    }

    private static async Task<PhysicalShardRecord> BindAsync(KeyLoadClient root, PartitionMovementPublicParentRf3Seed original,
        PartitionRef partition, CancellationToken token)
    {
        var target = original.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId
            != original.Directory.ControlOwner.PhysicalShardId).Owner;
        var before = await McpCallerAssertions.SdkSuccessAsync(await root.ReadAtomicPartitionPlacementAsync(
            new(ClusterRestoreMixedRetentionRf3Protocol.CurrentVersion, partition), token).ConfigureAwait(false));
        var bindId = Guid.NewGuid();
        var bind = new BindAtomicPartitionPlacementRequest(ClusterRestoreMixedRetentionRf3Protocol.CurrentVersion,
            before.DirectoryRevision, partition, target.PhysicalShardId);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.BindAtomicPartitionPlacementAsync(bindId, bind, token))).IsTrue();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.BindAtomicPartitionPlacementAsync(bindId, bind, token))).IsTrue();
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ReadAtomicPartitionPlacementAsync(
            new(ClusterRestoreMixedRetentionRf3Protocol.CurrentVersion, partition), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(ClusterRestoreMixedRetentionRf3Protocol.CurrentVersion,
            partition, target.PhysicalShardId, target.Incarnation, target.VoterIds, target.PlacementEpoch,
            before.DirectoryRevision + ClusterRestoreMixedRetentionRf3Protocol.FirstRevision,
            ClusterRestoreMixedRetentionRf3Protocol.FirstRevision, false), actual);
        var sourcePartition = partition with { PartitionKey = ClusterRestoreMixedRetentionRf3Protocol.SourceKey };
        var sourcePlacement = await McpCallerAssertions.SdkSuccessAsync(await root.ReadAtomicPartitionPlacementAsync(
            new(ClusterRestoreMixedRetentionRf3Protocol.CurrentVersion, sourcePartition), token));
        var sourceOwner = original.Directory.ControlOwner;
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(ClusterRestoreMixedRetentionRf3Protocol.CurrentVersion,
            sourcePartition, sourceOwner.PhysicalShardId, sourceOwner.Incarnation, sourceOwner.VoterIds, sourceOwner.PlacementEpoch,
            actual.DirectoryRevision, ClusterRestoreMixedRetentionRf3Protocol.NoRevision, true), sourcePlacement);
        return target;
    }

    private static PrincipalRecord Principal(PartitionRef partition, string id)
    {
        const Capability queue = Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect
            | Capability.DeadLettersRead | Capability.DeadLettersRedrive | Capability.QueueCancel | Capability.Query;
        const Capability topic = Capability.TopicsPublish | Capability.TopicsRead | Capability.SubscriptionsManage
            | Capability.SubscriptionsConsume | Capability.SubscriptionsAck | Capability.Query;
        return new(id, partition.TenantId,
            [new(partition.DatabaseId, QueueLifecyclePublicProtocol.Queue, queue),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Input, queue),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Output, queue),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Inbox, Capability.InboxWrite | Capability.Query),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Documents, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
             new(partition.DatabaseId, QueueLifecyclePublicProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Events, Capability.EventsRead | Capability.EventsAppend | Capability.Query),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Filtered, topic),
             new(partition.DatabaseId, ClusterRestoreMixedRetentionRf3Protocol.Purged, topic)], ["*"])
        { ClusterAdministrator = true };
    }

    private static async Task ResourcesAsync(KeyLoadClient root, PartitionRef partition, CancellationToken token)
    {
        var lifecycle = new ResourceDefinition(QueueLifecyclePublicProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueLifecyclePublicProtocol.One,
                MaxStoredMessages = QueueLifecyclePublicProtocol.Two,
                MaxDeadLetterMessages = QueueLifecyclePublicProtocol.One
            },
            FieldPolicies = [new("/knowledge", ClusterRestoreMixedRetentionRf3Protocol.PrivateGrant)],
            HeaderPolicies = [new("/source", ClusterRestoreMixedRetentionRf3Protocol.PrivateGrant)]
        };
        var inbox = new ResourceDefinition(ClusterRestoreMixedRetentionRf3Protocol.Inbox, ResourceKind.WorkQueue, partition.TransactionDomainId)
        { InboxPolicy = new(TargetInboxRf3Protocol.ReceiptCapacity, TargetInboxRf3Protocol.ByteCapacity) };
        var resources = new[]
        {
            lifecycle, inbox,
            new(QueueLifecyclePublicProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId),
            new(ClusterRestoreMixedRetentionRf3Protocol.Input, ResourceKind.WorkQueue, partition.TransactionDomainId),
            new(ClusterRestoreMixedRetentionRf3Protocol.Output, ResourceKind.WorkQueue, partition.TransactionDomainId),
            new(ClusterRestoreMixedRetentionRf3Protocol.Documents, ResourceKind.Collection, partition.TransactionDomainId),
            new(ClusterRestoreMixedRetentionRf3Protocol.Events, ResourceKind.StreamSet, partition.TransactionDomainId),
            new(ClusterRestoreMixedRetentionRf3Protocol.Filtered, ResourceKind.Topic, partition.TransactionDomainId),
            new(ClusterRestoreMixedRetentionRf3Protocol.Purged, ResourceKind.Topic, partition.TransactionDomainId)
        };
        foreach (var resource in resources)
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, resource), token).ConfigureAwait(false));
        }
    }

    internal static async Task CredentialAsync(KeyLoadClient root, PrincipalRecord principal, CancellationToken token)
    {
        _ = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token).ConfigureAwait(false));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(principal.Id, principal.Id, principal.Id + ClusterRestoreMixedRetentionRf3Protocol.CredentialSuffix), token))).IsTrue();
    }
}
