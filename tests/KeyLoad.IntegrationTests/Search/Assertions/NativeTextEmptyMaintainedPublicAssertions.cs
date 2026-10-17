using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextEmptyMaintainedPublicAssertions
{
    internal static async Task FirstAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceRequest request,
        ResourceDefinition resource, NodeStatus before, NodeStatus after)
    {
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await SqlRf3Protocol.EqualAsync(request.Consumer, actual.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(actual.Phase).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(actual.TrackedRecords).IsEqualTo(0);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(0L);
        await Assert.That(actual.ReleasedConsumer).IsNull();
        await Assert.That(actual.IndexSha256).IsNotNull();
        await Assert.That(actual.IndexSha256!.Length).IsEqualTo(64);
        await Assert.That(actual.Checkpoint).IsNotNull();
        await Assert.That(actual.Checkpoint!.Checkpoint).IsEqualTo(0L);
        await Assert.That(actual.Checkpoint.Receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Checkpoint.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Checkpoint.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(actual.Checkpoint.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Source).IsNotNull();
        var source = actual.Source!;
        await Assert.That(source.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(source.NodeId.ToString()).IsEqualTo(before.NodeId);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(source.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(source.ThroughSequence).IsEqualTo(0L);
        await Assert.That(source.SchemaVersion).IsEqualTo(1L);
        await Assert.That(source.PolicyEpoch).IsGreaterThan(0L);
        await Assert.That(source.ResourceSha256).IsEqualTo(Convert.ToHexStringLower(
            SHA256.HashData(NativeSerialization.Serialize(resource))));
        await Assert.That(source.Position).IsGreaterThan(0L);
        await Assert.That(source.AppliedPosition).IsGreaterThanOrEqualTo(before.Applied);
        await Assert.That(source.AppliedPosition).IsLessThanOrEqualTo(after.Applied);
        await Assert.That(source.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        await Assert.That(source.ReadGeneration).IsLessThanOrEqualTo(after.ReadGeneration);
    }

    internal static async Task PagesAsync(RequestCqrsRf3Callers caller,
        NativeTextEmptyMaintainedPublicState state, bool populated, bool selected, CancellationToken token)
    {
        var request = Request(state, selected);
        RankedDocument[] expected = populated
            ? [new(new(new(state.Scenario.Partition, state.Resource.Name, "first"),
                1L, "{\"text\":\"needle\"}", false, []), 1d / 61d, null)] : [];
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.SearchAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        NativeTextEmptyMaintainedPublicState state, OutboxStatus baseline, CancellationToken token)
    {
        await PagesAsync(caller, state, populated: false, selected: true, token);
        await PagesAsync(caller, state, populated: false, selected: false, token);
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, state.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(replay, state.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(replay.Source!, state.Built.Source!, before, after);
            await PagesAsync(caller, state, populated: false, selected: true, token);
            await PagesAsync(caller, state, populated: false, selected: false, token);
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, state.Scenario.Partition, baseline, token);
        }
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers caller,
        NativeTextEmptyMaintainedPublicState state, OutboxStatus baseline, CancellationToken token)
    {
        var sdk = await caller.Sdk.SearchAsync(Request(state, selected: true), token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.HistoryUnavailable.ToString());
        await Assert.That(sdk.Value).IsNull();
        await PagesAsync(caller, state, populated: true, selected: false, token);
        await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, state.Scenario.Partition, baseline, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.SearchExecute,
            Request(state, selected: true), token), ErrorCode.HistoryUnavailable, dispatched: true);
        await PagesAsync(caller, state, populated: true, selected: false, token);
        await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, state.Scenario.Partition, baseline, token);
    }

    private static SearchRequest Request(NativeTextEmptyMaintainedPublicState state, bool selected)
        => new(state.Scenario.Partition, state.Resource.Name, NativeTextMaintenanceRf3Scenario.Field, "needle",
            TextIndex: selected ? new(state.Build.Consumer, state.Build.IndexGeneration) : null);
}
