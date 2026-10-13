using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedExplainPublicTrial
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
                    await NativeTextSelectedExplainPublicReplay.RunAsync(caller, state, token);
                    return true;
                }, token);
        }
    }

    private static async Task<NativeTextSelectedExplainPublicState> PrepareAsync(
        RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(scenario.Partition.TenantId,
                scenario.Partition.DatabaseId, resource), token)));
        var published = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(resource.Name, "a", NativeTextSelectedExplainPublicPages.FirstJson, 0L),
             new PutDocument(resource.Name, "b", NativeTextSelectedExplainPublicPages.SecondJson, 0L)]);
        var publication = await CommitAsync(caller, published,
            [new("putDocument", resource.Name, "a", 1L), new("putDocument", resource.Name, "b", 1L)], token);
        var build = await scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
        var built = await MaintainAsync(caller, build, resource, 2L, token);
        var warm = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await WarmReplayAsync(caller, build, built, warm, token);
        var deleted = new CommandRequest(Guid.NewGuid(), scenario.Partition, [new DeleteDocument(resource.Name, "b", 1L)]);
        var deletion = await CommitAsync(caller, deleted, [new("deleteDocument", resource.Name, "b", 2L)], token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.WaitForIndexAsync(
            new(scenario.Partition, resource.Name, NativeTextMaintenanceRf3Scenario.Field, deletion.Token), token));
        var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await NativeTextSelectedExplainPublicPages.StaleAsync(caller, build, baseline, token);
        var restore = build with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
        var restored = await MaintainAsync(caller, restore, resource, 3L, token);
        var currentOutbox = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var status = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        var state = new NativeTextSelectedExplainPublicState(resource, build, built, restore, restored,
            published, publication, deleted, deletion, currentOutbox, status);
        await NativeTextSelectedExplainPublicReplay.RunAsync(caller, state, token);
        return state;
    }

    private static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers caller,
        CommandRequest command, MutationReceipt[] expected, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(1, command.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await caller.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value;
        await SqlRf3Protocol.EqualAsync(placement, official);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
        return receipt;
    }

    private static async Task<TextIndexMaintenanceResult> MaintainAsync(RequestCqrsRf3Callers caller,
        TextIndexMaintenanceRequest request, ResourceDefinition resource, long sequence, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        var result = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, request,
            NativeTextMaintenancePath.Mcp, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        await NativeTextSelectedExplainPublicMaintenance.FirstAsync(result, request, resource, sequence, before, after);
        return result;
    }

    private static async Task WarmReplayAsync(RequestCqrsRf3Callers caller, TextIndexMaintenanceRequest request,
        TextIndexMaintenanceResult original, OutboxStatus baseline, CancellationToken token)
    {
        await NativeTextSelectedExplainPublicPages.CurrentAsync(caller, request, false, true, baseline, token);
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, request, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(replay, original);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(replay.Source!, original.Source!, before, after);
            await NativeTextSelectedExplainPublicPages.CurrentAsync(caller, request, false, true, baseline, token);
        }
    }
}
