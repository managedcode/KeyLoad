using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextNonStringTransitionsPublicTrial
{
    internal const string Collection = "scalar-text-shapes";
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var originals = new List<Original>();
        var prepared = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var partition = TextMaintenanceClusterScenario.CreatePartition();
                var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId);
                await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
                originals.Add(await CommitAsync(caller, new(Guid.NewGuid(), partition,
                    [new PutDocument(Collection, "a", "{\"text\":\"needle\"}", 0L),
                     new PutDocument(Collection, "b", "{\"text\":null}", 0L),
                     new PutDocument(Collection, "c", "{\"text\":42}", 0L),
                     new PutDocument(Collection, "d", "{\"other\":\"needle\"}", 0L)]),
                    [new("putDocument", Collection, "a", 1L), new("putDocument", Collection, "b", 1L),
                     new("putDocument", Collection, "c", 1L), new("putDocument", Collection, "d", 1L)], token));
                await TextNonStringTransitionsPublicAssertions.CheckAsync(caller, partition, originals, 0, token);
                originals.Add(await CommitAsync(caller, new(Guid.NewGuid(), partition,
                    [new PutDocument(Collection, "b", "{\"text\":\"needle needle\"}", 1L)]),
                    [new("putDocument", Collection, "b", 2L)], token));
                var outbox = await TextNonStringTransitionsPublicAssertions.CheckAsync(caller, partition, originals, 1, token);
                return (Partition: partition, Outbox: outbox,
                    Status: await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token)));
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await TextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var current = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    await Assert.That(current.NodeId).IsEqualTo(prepared.Status.NodeId);
                    await Assert.That(current.Incarnation).IsEqualTo(prepared.Status.Incarnation);
                    await Assert.That(current.Position).IsGreaterThanOrEqualTo(prepared.Status.Position);
                    await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(prepared.Status.ReadGeneration);
                    await TextNonStringTransitionsPublicAssertions.ReplayAsync(caller, prepared.Partition,
                        originals, 1, prepared.Outbox, token);
                    return true;
                }, token);
        }
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async caller =>
        {
            originals.Add(await CommitAsync(caller, new(Guid.NewGuid(), prepared.Partition,
                [new PutDocument(Collection, "a", "{\"text\":0}", 1L)]), [new("putDocument", Collection, "a", 2L)], token));
            await TextNonStringTransitionsPublicAssertions.CheckAsync(caller, prepared.Partition, originals, 2, token);
            originals.Add(await CommitAsync(caller, new(Guid.NewGuid(), prepared.Partition,
                [new DeleteDocument(Collection, "b", 2L)]), [new("deleteDocument", Collection, "b", 3L)], token));
            await TextNonStringTransitionsPublicAssertions.CheckAsync(caller, prepared.Partition, originals, 3, token);
            return true;
        }, token);
    }

    private static async Task<Original> CommitAsync(RequestCqrsClusterCallers caller, CommandRequest command,
        MutationReceipt[] expected, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(expected, receipt.Mutations.ToArray());
        var request = new WaitForIndexRequest(command.Partition, Collection, "/text", receipt.Token);
        foreach (var wait in new[] { await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.WaitForIndexAsync(request, token)),
            (await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(await caller.Mcp.CallAsync(WaitForIndexProtocol.Tool, request, token))).Value })
        {
            await Assert.That(wait.SchemaVersion).IsEqualTo(1L);
            await Assert.That(wait.PolicyEpoch).IsGreaterThan(0L);
            await Assert.That(wait.AppliedToken.Incarnation).IsEqualTo(receipt.Token.Incarnation);
            await Assert.That(wait.AppliedToken.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await Assert.That(wait.AppliedToken.Position).IsGreaterThanOrEqualTo(receipt.Token.Position);
        }
        return new(command, receipt);
    }
}
