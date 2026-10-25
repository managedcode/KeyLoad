using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class CapabilityOutputClusterSetup
{
    internal static Task<CapabilityOutputClusterState> CreateAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async caller =>
        {
            var partition = PostgresClusterState.Partition;
            var definition = new ResourceDefinition(CapabilityOutputClusterProtocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId);
            var creationId = Guid.NewGuid();
            var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
            var resource = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ConfigureResourceAsync(creationId, creation, token));
            await SqlClusterProtocol.EqualAsync(definition, resource);
            var reader = await PostgresClusterSeed.IdentityAsync(caller.Sdk, "capability-output-",
                Capability.DocumentsRead | Capability.Query, token, definition.Name);
            var seeds = new List<(CommandRequest Command, CommitReceipt Receipt)>();
            foreach (var id in new[] { "a", "b" })
            {
                var command = new CommandRequest(Guid.NewGuid(), partition,
                    [new PutDocument(definition.Name, id, CapabilityOutputClusterProtocol.Json(id))]);
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
                await CapabilityOutputClusterAssertions.ReceiptAsync(fixture, command, receipt);
                seeds.Add((command, receipt));
            }
            var manifest = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
            return new CapabilityOutputClusterState(reader, [.. seeds], manifest, ResourceCreationLifetime.Create(creationId, creation));
        }, token);
}
