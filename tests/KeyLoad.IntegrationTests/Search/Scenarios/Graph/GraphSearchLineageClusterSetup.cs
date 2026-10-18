using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageClusterSetup
{
    internal static async Task<GraphSearchLineageClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        bool expansion, bool emptyLabels, CancellationToken token)
    {
        var tenant = GraphSearchLineageClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, GraphSearchLineageClusterProtocol.Database, GraphSearchLineageClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources =
        [new(GraphSearchLineageClusterProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId),
         new(GraphSearchLineageClusterProtocol.EarlyGraph, ResourceKind.Graph, partition.TransactionDomainId),
         new(GraphSearchLineageClusterProtocol.LateGraph, ResourceKind.Graph, partition.TransactionDomainId)
         {
             FieldPolicies = [new(GraphSearchLineageClusterProtocol.LabelPath, GraphSearchLineageClusterProtocol.Classification,
                 GraphSearchLineageClusterProtocol.LabelRead, GraphSearchLineageClusterProtocol.LabelUse)]
         }];
        foreach (var resource in resources)
        {
            await SqlClusterProtocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                Guid.NewGuid(), new(tenant, partition.DatabaseId, resource), token)), resource);
        }
        var seed = GraphSearchLineageClusterRecipe.Seed(partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.Token.Position > 0L).IsTrue();
        await SqlClusterProtocol.EqualAsync(receipt, new CommitReceipt(seed.CommandId, new(receipt.Token.Incarnation,
            partition.AtomicPartitionId, receipt.Token.Position), GraphSearchLineageClusterRecipe.Receipts,
            DurabilityProfile.ProcessDurable, 3));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(partition, token));
        await AtomicProducerClusterOutbox.LiteralAsync(outbox, seed.Mutations.Length);
        await Assert.That(seed.Mutations.Length).IsEqualTo(8);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, GraphSearchLineageClusterProtocol.Collection, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch),
             new(partition.DatabaseId, GraphSearchLineageClusterProtocol.EarlyGraph, Capability.GraphRead),
             new(partition.DatabaseId, GraphSearchLineageClusterProtocol.LateGraph, Capability.GraphRead)], [], false, token);
        return new(partition, identity, seed, receipt, outbox, expansion, emptyLabels);
    }
    internal static Task PolicyAsync(ClusterFixture fixture, GraphSearchLineageClusterState state,
        long epoch, bool allow, CancellationToken token)
        => MessagingClusterIdentity.UpdateAsync(fixture, state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = allow ? [GraphSearchLineageClusterProtocol.LabelUse] : []
        }, token);
}
