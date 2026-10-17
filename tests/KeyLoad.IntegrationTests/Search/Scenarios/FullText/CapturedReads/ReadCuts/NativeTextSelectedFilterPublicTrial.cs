using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedFilterPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, caller => PrepareAsync(caller, token), token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var current = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    await Assert.That(current.NodeId).IsEqualTo(state.Status.NodeId);
                    await Assert.That(current.Incarnation).IsEqualTo(state.Status.Incarnation);
                    await Assert.That(current.Applied).IsGreaterThanOrEqualTo(state.Status.Applied);
                    await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(state.Status.ReadGeneration);
                    await NativeTextSelectedFilterPublicAssertions.ReplayAsync(caller, state, token);
                    return true;
                }, token);
        }
    }

    private static async Task<NativeTextSelectedFilterPublicState> PrepareAsync(
        RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(scenario.Partition.TenantId,
                scenario.Partition.DatabaseId, resource), token)));
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(resource.Name, "a", NativeTextSelectedFilterPublicAssertions.FirstJson, 0L),
             new PutDocument(resource.Name, "b", NativeTextSelectedFilterPublicAssertions.SecondJson, 0L),
             new PutDocument(resource.Name, "c", NativeTextSelectedFilterPublicAssertions.ThirdJson, 0L)]);
        var receipt = await CommitAsync(caller, command, token);
        var request = await scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, request,
            NativeTextMaintenancePath.Mcp, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        await NativeTextSelectedFilterPublicAssertions.FirstAsync(built, request, resource, before, after);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new NativeTextSelectedFilterPublicState(resource, command, receipt, request, built, outbox, after);
        await NativeTextSelectedFilterPublicAssertions.ReplayAsync(caller, state, token);
        return state;
    }

    private static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers caller,
        CommandRequest command, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(1, command.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await caller.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        MutationReceipt[] expected = [new("putDocument", NativeTextMaintenanceRf3Scenario.Collection, "a", 1L),
            new("putDocument", NativeTextMaintenanceRf3Scenario.Collection, "b", 1L),
            new("putDocument", NativeTextMaintenanceRf3Scenario.Collection, "c", 1L)];
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
        return receipt;
    }
}
