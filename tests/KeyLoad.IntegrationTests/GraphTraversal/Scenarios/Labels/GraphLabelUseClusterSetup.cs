using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphLabelUseClusterSetup
{
    internal static Task<GraphLabelUseClusterState> CreateAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var partition = PostgresClusterState.Partition;
            foreach (var name in new[] { GraphLabelUseClusterProtocol.Collection, GraphLabelUseClusterProtocol.Graph })
            {
                var graph = name == GraphLabelUseClusterProtocol.Graph;
                var resource = new ResourceDefinition(name, graph ? ResourceKind.Graph : ResourceKind.Collection,
                    partition.TransactionDomainId)
                {
                    FieldPolicies = graph ? [new("/label", "private", GraphLabelUseClusterProtocol.ReadGrant,
                        GraphLabelUseClusterProtocol.UseGrant)] : []
                };
                await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
            }
            var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, GraphLabelUseClusterProtocol.Graph,
                Capability.GraphRead | Capability.Query, token);
            var principal = identity.Principal with
            {
                PolicyEpoch = identity.Principal.PolicyEpoch + 1L,
                Grants = [new(partition.DatabaseId, GraphLabelUseClusterProtocol.Collection, Capability.DocumentsRead | Capability.Query),
                    new(partition.DatabaseId, GraphLabelUseClusterProtocol.Graph, Capability.GraphRead | Capability.Query)],
                FieldGrants = [GraphLabelUseClusterProtocol.ReadGrant]
            };
            var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
            await SqlClusterProtocol.EqualAsync(principal, persisted);
            var command = new CommandRequest(Guid.NewGuid(), partition,
                [new PutDocument(GraphLabelUseClusterProtocol.Collection, "a", "{}"),
                    new PutDocument(GraphLabelUseClusterProtocol.Collection, "b", "{}"),
                    new PutDocument(GraphLabelUseClusterProtocol.Collection, "c", "{}"),
                    .. GraphLabelUseClusterProtocol.Edges(partition).Select(edge => new UpsertEdge(GraphLabelUseClusterProtocol.Graph,
                        edge.Id, edge.From, edge.To, edge.Label, edge.AttributesJson))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
            await GraphLabelUseClusterAssertions.ReceiptAsync(fixture, command, receipt);
            return new GraphLabelUseClusterState(identity with
            {
                Principal = persisted
            }, command, receipt);
        }, token);
}
