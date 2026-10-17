using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseRf3Setup
{
    internal static Task<GraphLabelUseRf3State> CreateAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var partition = PostgresRf3State.Partition;
            foreach (var name in new[] { GraphLabelUseRf3Protocol.Collection, GraphLabelUseRf3Protocol.Graph })
            {
                var graph = name == GraphLabelUseRf3Protocol.Graph;
                var resource = new ResourceDefinition(name, graph ? ResourceKind.Graph : ResourceKind.Collection,
                    partition.TransactionDomainId)
                {
                    FieldPolicies = graph ? [new("/label", "private", GraphLabelUseRf3Protocol.ReadGrant,
                        GraphLabelUseRf3Protocol.UseGrant)] : []
                };
                await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
            }
            var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, GraphLabelUseRf3Protocol.Graph,
                Capability.GraphRead | Capability.Query, token);
            var principal = identity.Principal with
            {
                PolicyEpoch = identity.Principal.PolicyEpoch + 1L,
                Grants = [new(partition.DatabaseId, GraphLabelUseRf3Protocol.Collection, Capability.DocumentsRead | Capability.Query),
                    new(partition.DatabaseId, GraphLabelUseRf3Protocol.Graph, Capability.GraphRead | Capability.Query)],
                FieldGrants = [GraphLabelUseRf3Protocol.ReadGrant]
            };
            var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
            await SqlRf3Protocol.EqualAsync(principal, persisted);
            var command = new CommandRequest(Guid.NewGuid(), partition,
                [new PutDocument(GraphLabelUseRf3Protocol.Collection, "a", "{}"),
                    new PutDocument(GraphLabelUseRf3Protocol.Collection, "b", "{}"),
                    new PutDocument(GraphLabelUseRf3Protocol.Collection, "c", "{}"),
                    .. GraphLabelUseRf3Protocol.Edges(partition).Select(edge => new UpsertEdge(GraphLabelUseRf3Protocol.Graph,
                        edge.Id, edge.From, edge.To, edge.Label, edge.AttributesJson))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
            await GraphLabelUseRf3Assertions.ReceiptAsync(fixture, command, receipt);
            return new GraphLabelUseRf3State(identity with
            {
                Principal = persisted
            }, command, receipt);
        }, token);
}
