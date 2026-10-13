using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextPartitionGenerationPublicAssertions
{
    internal static async Task FirstAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceRequest request,
        NodeStatus before, NodeStatus after)
    {
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await SqlRf3Protocol.EqualAsync(request.Consumer, actual.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(1L);
        await Assert.That(actual.Phase).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(actual.TrackedRecords).IsEqualTo(1);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(1L);
        await Assert.That(actual.ReleasedConsumer).IsNull();
        await Assert.That(actual.IndexSha256).IsNotNull();
        await Assert.That(actual.IndexSha256!.Length).IsEqualTo(64);
        await Assert.That(actual.Checkpoint).IsNotNull();
        await Assert.That(actual.Checkpoint!.Checkpoint).IsEqualTo(1L);
        await Assert.That(actual.Checkpoint.Receipt.Mutations.IsDefault).IsFalse();
        await SqlRf3Protocol.EqualAsync(Array.Empty<MutationReceipt>(), actual.Checkpoint.Receipt.Mutations.ToArray());
        await Assert.That(actual.Checkpoint.Receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Checkpoint.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(actual.Checkpoint.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Checkpoint.Receipt.Token.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(actual.Checkpoint.Receipt.Token.OwnershipEpoch).IsEqualTo(request.Placement.PlacementEpoch);
        await Assert.That(actual.Checkpoint.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Source).IsNotNull();
        await Assert.That(actual.Source!.ThroughSequence).IsEqualTo(1L);
        await Assert.That(actual.Source.SchemaVersion).IsEqualTo(1L);
        await Assert.That(actual.Source.PolicyEpoch).IsGreaterThan(0L);
        await Assert.That(actual.Source.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(actual.Source.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(actual.Source, actual.Source, before, after);
    }

    internal static async Task ControlsAsync(RequestCqrsRf3Callers caller,
        NativeTextPartitionGenerationPublicState[] states, CancellationToken token)
    {
        foreach (var state in states)
        {
            RankedDocument[] expected = [new(new(new(state.Partition, NativeTextMaintenanceRf3Scenario.Collection,
                NativeTextPartitionGenerationPublicSeed.Id), 1L, state.Json, false, []), 1d / 61d, null)];
            foreach (var selected in new[] { false, true })
            {
                var request = Request(state, selected);
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.SearchAsync(request, token)));
                await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
                    await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
            }
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, state.Partition, state.Outbox, token);
        }
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers caller,
        NativeTextPartitionGenerationPublicState current, NativeTextPartitionGenerationPublicState foreign,
        NativeTextPartitionGenerationPublicState[] states, CancellationToken token)
    {
        var wrong = Request(current, true) with { TextIndex = new(foreign.Build.Consumer, foreign.Build.IndexGeneration) };
        var sdk = await caller.Sdk.SearchAsync(wrong, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await ControlsAsync(caller, states, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, wrong, token),
            ErrorCode.Validation, dispatched: true);
        await ControlsAsync(caller, states, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        NativeTextPartitionGenerationPublicState state, NativeTextPartitionGenerationPublicState[] states,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(state.Command, token)));
        await ControlsAsync(caller, states, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await ControlsAsync(caller, states, token);
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var actual = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, state.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(actual, state.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(actual.Source!, state.Built.Source!, before, after);
            await ControlsAsync(caller, states, token);
        }
    }

    private static SearchRequest Request(NativeTextPartitionGenerationPublicState state, bool selected)
        => new(state.Partition, NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.Field,
            NativeTextPartitionGenerationPublicSeed.Term,
            TextIndex: selected ? new(state.Build.Consumer, state.Build.IndexGeneration) : null);
}
