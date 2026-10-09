using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingSeed
{
    internal static async Task<ClusterRestoreRf3EventingState> CreateAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(ClusterRestoreRf3Protocol.CurrentVersion, seed.Partition), cancellationToken).ConfigureAwait(false));
        var node = placement.PhysicalShardId == source.Profile.PhysicalShardId
            ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
        return await ClusterRestoreRf3CallerOwner.RunAsync(source.Application, node, seed.Credential,
            (sdk, official) => CreateOwnedAsync(sdk, official, seed.Partition, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ClusterRestoreRf3EventingState> CreateOwnedAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var state = new ClusterRestoreRf3EventingState(partition);
        foreach (var (resource, kind) in new[] { (ClusterRestoreRf3EventingProtocol.Topic, ResourceKind.Topic),
            (ClusterRestoreRf3EventingProtocol.Queue, ResourceKind.WorkQueue),
            (ClusterRestoreRf3EventingProtocol.Documents, ResourceKind.Collection) })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(resource, kind, partition.TransactionDomainId)),
                cancellationToken).ConfigureAwait(false));
        }
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(new(Guid.NewGuid(), partition,
            [new PublishTopic(ClusterRestoreRf3EventingProtocol.Topic,
                [new(ClusterRestoreRf3EventingProtocol.First, ClusterRestoreRf3EventingProtocol.EventType, ClusterRestoreRf3EventingProtocol.Json),
                 new(ClusterRestoreRf3EventingProtocol.Second, ClusterRestoreRf3EventingProtocol.EventType, ClusterRestoreRf3EventingProtocol.Json),
                 new(ClusterRestoreRf3EventingProtocol.Third, ClusterRestoreRf3EventingProtocol.EventType, ClusterRestoreRf3EventingProtocol.Json)])]),
            cancellationToken).ConfigureAwait(false));
        foreach (var group in new[] { state.Group, state.Other })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureSubscriptionAsync(new(Guid.NewGuid(), group,
                new(PartitionMovementPublicParentRf3Administrator.PrincipalId)), cancellationToken).ConfigureAwait(false));
        }
        state.Received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(
            new(Guid.NewGuid(), state.Group, MaxEvents: ClusterRestoreRf3EventingProtocol.SourceEvents), cancellationToken).ConfigureAwait(false));
        await Assert.That(state.Received.Deliveries.Length).IsEqualTo(ClusterRestoreRf3EventingProtocol.SourceEvents);
        state.ProcessRequest = new(Guid.NewGuid(), state.Group,
            state.Received.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].Token,
            ClusterRestoreRf3EventingProtocol.Handler, ClusterRestoreRf3EventingProtocol.InitialGeneration, Effects());
        state.Processed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitSubscriptionProcessingAsync(
            state.ProcessRequest, cancellationToken).ConfigureAwait(false));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteSubscriptionAsync(new(Guid.NewGuid(), state.Group,
            state.Received.Deliveries[ClusterRestoreRf3EventingProtocol.ThirdIndex].Token, DeliveryAction.Ack), cancellationToken).ConfigureAwait(false));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(new(Guid.NewGuid(), state.Other),
            cancellationToken).ConfigureAwait(false));
        state.QueueClaim = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(new(Guid.NewGuid(), state.Lane),
            cancellationToken).ConfigureAwait(false));
        state.Page = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(state.Source), cancellationToken).ConfigureAwait(false));
        state.Message = (await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(new(state.Lane,
            ClusterRestoreRf3EventingProtocol.MessageId), cancellationToken).ConfigureAwait(false)))!;
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(partition, cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3EventingSourceOracle.RequireAsync(sdk, state, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(state.Processed,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitSubscriptionProcessingAsync(state.ProcessRequest,
                cancellationToken).ConfigureAwait(false)), sdk, official, partition,
            McpCallerTools.SubscriptionsProcess, state.ProcessRequest, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3EventingReadOracle.RequireAsync(sdk, official, state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    internal static System.Collections.Immutable.ImmutableArray<Mutation> Effects()
        => [new PutDocument(ClusterRestoreRf3EventingProtocol.Documents, ClusterRestoreRf3EventingProtocol.DocumentId,
            ClusterRestoreRf3EventingProtocol.Json, ClusterRestoreRf3EventingProtocol.EmptyCheckpoint),
            new EnqueueMessage(ClusterRestoreRf3EventingProtocol.Queue, ClusterRestoreRf3EventingProtocol.MessageId,
                ClusterRestoreRf3EventingProtocol.Json)];
}
