using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextEmptyMaintainedPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var prepared = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
                var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
                    ResourceKind.Collection, scenario.Partition.TransactionDomainId);
                await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(scenario.Partition.TenantId,
                        scenario.Partition.DatabaseId, resource), token)));
                var request = await scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
                var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp,
                    request, NativeTextMaintenancePath.Mcp, token);
                var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                await NativeTextEmptyMaintainedPublicAssertions.FirstAsync(built, request, resource, before, after);
                var state = new NativeTextEmptyMaintainedPublicState(scenario, resource, request, built);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
                await NativeTextEmptyMaintainedPublicAssertions.ReplayAsync(caller, state, baseline, token);
                return (State: state, Outbox: baseline, Status: after);
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var current = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    await Assert.That(current.NodeId).IsEqualTo(prepared.Status.NodeId);
                    await Assert.That(current.Incarnation).IsEqualTo(prepared.Status.Incarnation);
                    await Assert.That(current.Applied).IsGreaterThanOrEqualTo(prepared.Status.Applied);
                    await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(prepared.Status.ReadGeneration);
                    await NativeTextEmptyMaintainedPublicAssertions.ReplayAsync(caller, prepared.State, prepared.Outbox, token);
                    return true;
                }, token);
        }
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                await PublishAsync(caller, prepared.State, token);
                return true;
            }, token);
    }

    private static async Task PublishAsync(RequestCqrsRf3Callers caller,
        NativeTextEmptyMaintainedPublicState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new PutDocument(state.Resource.Name, "first", "{\"text\":\"needle\"}", 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Scenario.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        MutationReceipt[] mutations = [new("putDocument", state.Resource.Name, "first", 1L)];
        await SqlRf3Protocol.EqualAsync(mutations, receipt.Mutations.ToArray());
        _ = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.WaitForIndexAsync(
            new(state.Scenario.Partition, state.Resource.Name, NativeTextMaintenanceRf3Scenario.Field, receipt.Token), token));
        var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await NativeTextEmptyMaintainedPublicAssertions.RefuseAsync(caller, state, baseline, token);
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
        await NativeTextEmptyMaintainedPublicAssertions.RefuseAsync(caller, state, baseline, token);
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await NativeTextEmptyMaintainedPublicAssertions.RefuseAsync(caller, state, baseline, token);
    }
}
