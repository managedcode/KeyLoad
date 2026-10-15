using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class CapabilityOutputRf3Setup
{
    internal static Task<CapabilityOutputRf3State> CreateAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async caller =>
        {
            var partition = PostgresRf3State.Partition;
            var definition = new ResourceDefinition(CapabilityOutputRf3Protocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId);
            var resource = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), token));
            await SqlRf3Protocol.EqualAsync(definition, resource);
            var reader = await PostgresRf3Seed.IdentityAsync(caller.Sdk, "capability-output-",
                Capability.DocumentsRead | Capability.Query, token, definition.Name);
            var seeds = new List<(CommandRequest Command, CommitReceipt Receipt)>();
            foreach (var id in new[] { "a", "b" })
            {
                var command = new CommandRequest(Guid.NewGuid(), partition,
                    [new PutDocument(definition.Name, id, CapabilityOutputRf3Protocol.Json(id))]);
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
                await CapabilityOutputRf3Assertions.ReceiptAsync(fixture, command, receipt);
                seeds.Add((command, receipt));
            }
            var manifest = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
            return new CapabilityOutputRf3State(reader, [.. seeds], manifest);
        }, token);
}
