using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class CompetingInboxPublicSeed
{
    internal static async Task RunAsync(CompetingInboxPublicState state, RequestCqrsRf3Callers root, CancellationToken token)
    {
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(state.Fixture.Root);
        var expectedPlacement = new AtomicPartitionPlacementResolution(CompetingInboxPublicProtocol.PlacementVersion, state.Partition, state.Fixture.PhysicalShardId,
            profile.Incarnation, [RequestCqrsProbeFixtureProtocol.Node1Origin, RequestCqrsProbeFixtureProtocol.Node2Origin,
                RequestCqrsProbeFixtureProtocol.Node3Origin], CompetingInboxPublicProtocol.OriginalOwnershipEpoch, 0L, 0L, true);
        state.Placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(CompetingInboxPublicProtocol.PlacementVersion, state.Partition), token));
        await CompetingInboxPublicValues.EqualAsync(expectedPlacement, state.Placement);
        foreach (var (name, kind) in new[]
        {
            (CompetingInboxPublicProtocol.Input, ResourceKind.WorkQueue),
            (CompetingInboxPublicProtocol.Output, ResourceKind.WorkQueue),
            (CompetingInboxPublicProtocol.Documents, ResourceKind.Collection),
            (CompetingInboxPublicProtocol.Streams, ResourceKind.StreamSet)
        })
        {
            var resource = new ResourceDefinition(name, kind, state.Partition.TransactionDomainId)
            {
                FieldPolicies = name == CompetingInboxPublicProtocol.Input ? [Required()] : [],
                HeaderPolicies = name == CompetingInboxPublicProtocol.Input ? [Required()] : []
            };
            await CompetingInboxPublicValues.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(state.Partition.TenantId, state.Partition.DatabaseId, resource), token)));
        }
        state.Identity = await MessagingRf3Identity.CreateAsync(state.Fixture, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, CompetingInboxPublicProtocol.Input, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(state.Partition.DatabaseId, CompetingInboxPublicProtocol.Output, Capability.QueuePublish | Capability.QueueInspect),
             new(state.Partition.DatabaseId, CompetingInboxPublicProtocol.Documents, Capability.DocumentsRead | Capability.DocumentsWrite),
             new(state.Partition.DatabaseId, CompetingInboxPublicProtocol.Streams, Capability.EventsRead | Capability.EventsAppend)],
            [CompetingInboxPublicProtocol.RequiredRead], false, token);
        await EnqueueAsync(state, root, CompetingInboxPublicProtocol.First, token);
        await CompetingInboxPublicModels.OutboxAsync(state, root, CompetingInboxPublicProtocol.InitialOutboxRecords, true, token);
    }
    private static SensitiveFieldPolicy Required()
        => new(CompetingInboxPublicProtocol.Field, CompetingInboxPublicProtocol.Classification,
            CompetingInboxPublicProtocol.RequiredRead, RequiredForProcessing: true);
    internal static async Task EnqueueAsync(CompetingInboxPublicState state, RequestCqrsRf3Callers root, string id, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(CompetingInboxPublicProtocol.Input, id, CompetingInboxPublicProtocol.Payload, CompetingInboxPublicProtocol.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(request, token));
        state.Producers.Add((request, receipt));
        await CompetingInboxPublicValues.ReceiptAsync(state, receipt, request.CommandId,
            [new(CompetingInboxPublicProtocol.EnqueueKind, CompetingInboxPublicProtocol.Input, id, 1L)]);
    }
    internal static async Task<Delivery> ReceiveAsync(CompetingInboxPublicState state, RequestCqrsRf3Callers worker,
        string id, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var before = state.Clock.GetUtcNow();
        var result = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.ReceiveAsync(request, token));
        var after = state.Clock.GetUtcNow();
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        await Assert.That(result.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(result.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        var expected = new Delivery(id, CompetingInboxPublicProtocol.Payload, CompetingInboxPublicProtocol.Headers,
            delivery.Token, 1L, delivery.LeaseUntil, 1, 1L);
        await CompetingInboxPublicValues.EqualAsync(expected, delivery);
        await Assert.That(string.IsNullOrWhiteSpace(delivery.Token)).IsFalse();
        await Assert.That(delivery.LeaseUntil).IsGreaterThanOrEqualTo(before.AddSeconds(request.LeaseSeconds));
        await Assert.That(delivery.LeaseUntil).IsLessThanOrEqualTo(after.AddSeconds(request.LeaseSeconds));
        return delivery;
    }
}
