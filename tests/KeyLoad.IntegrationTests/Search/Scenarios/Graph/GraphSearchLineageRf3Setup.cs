using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageRf3Setup
{
    internal static async Task<GraphSearchLineageRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        bool expansion, bool emptyLabels, CancellationToken token)
    {
        var tenant = GraphSearchLineageRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, GraphSearchLineageRf3Protocol.Database, GraphSearchLineageRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources =
        [new(GraphSearchLineageRf3Protocol.Collection, ResourceKind.Collection, partition.TransactionDomainId),
         new(GraphSearchLineageRf3Protocol.EarlyGraph, ResourceKind.Graph, partition.TransactionDomainId),
         new(GraphSearchLineageRf3Protocol.LateGraph, ResourceKind.Graph, partition.TransactionDomainId)
         {
             FieldPolicies = [new(GraphSearchLineageRf3Protocol.LabelPath, GraphSearchLineageRf3Protocol.Classification,
                 GraphSearchLineageRf3Protocol.LabelRead, GraphSearchLineageRf3Protocol.LabelUse)]
         }];
        foreach (var resource in resources)
        {
            await SqlRf3Protocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                Guid.NewGuid(), new(tenant, partition.DatabaseId, resource), token)), resource);
        }
        var seed = GraphSearchLineageRf3Recipe.Seed(partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(GraphSearchLineageRf3Protocol.Version, partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.Token.Position > 0L).IsTrue();
        await SqlRf3Protocol.EqualAsync(receipt, new CommitReceipt(seed.CommandId, new(placement.Incarnation,
            partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch), GraphSearchLineageRf3Recipe.Receipts,
            DurabilityProfile.QuorumProcessDurable));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(partition, token));
        await AtomicProducerRf3Outbox.LiteralAsync(outbox, seed.Mutations.Length);
        await Assert.That(seed.Mutations.Length).IsEqualTo(8);
        var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, GraphSearchLineageRf3Protocol.Collection, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch),
             new(partition.DatabaseId, GraphSearchLineageRf3Protocol.EarlyGraph, Capability.GraphRead),
             new(partition.DatabaseId, GraphSearchLineageRf3Protocol.LateGraph, Capability.GraphRead)], [], false, token);
        return new(partition, identity, seed, receipt, outbox, expansion, emptyLabels);
    }
    internal static Task PolicyAsync(ClusterFixture fixture, GraphSearchLineageRf3State state,
        long epoch, bool allow, CancellationToken token)
        => MessagingRf3Identity.UpdateAsync(fixture, state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = allow ? [GraphSearchLineageRf3Protocol.LabelUse] : []
        }, token);
}
