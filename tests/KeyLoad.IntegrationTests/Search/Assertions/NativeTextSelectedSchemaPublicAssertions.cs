using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedSchemaPublicAssertions
{
    internal static async Task PagesAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState state, bool selected, CancellationToken token)
    {
        foreach (var (query, id, json) in new[]
        {
            ("ПРИВІТ", NativeTextMaintenanceRf3Scenario.Ukrainian, NativeTextMaintenanceRf3Scenario.UkrainianJson),
            ("hello", NativeTextMaintenanceRf3Scenario.English, NativeTextMaintenanceRf3Scenario.EnglishJson)
        })
        {
            var request = Request(state, query, selected);
            RankedDocument[] expected = [new(new(new(state.Scenario.Partition, state.Resource.Name, id),
                1L, json, false, []), 1d / 61d, null)];
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.SearchAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
                await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
        }
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState state, OutboxStatus baseline, CancellationToken token)
    {
        var request = Request(state, "ПРИВІТ", selected: true);
        var sdk = await caller.Sdk.SearchAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.HistoryUnavailable.ToString());
        await Assert.That(sdk.Value).IsNull();
        await PagesAsync(caller, state, selected: false, token);
        await OutboxAsync(caller, state.Scenario.Partition, baseline, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
            ErrorCode.HistoryUnavailable, dispatched: true);
        await PagesAsync(caller, state, selected: false, token);
        await OutboxAsync(caller, state.Scenario.Partition, baseline, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState state, OutboxStatus baseline, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.CommitAsync(state.Original, token)));
        await RefuseAsync(caller, state, baseline, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original, token))).Value);
        await RefuseAsync(caller, state, baseline, token);
    }

    internal static async Task OutboxAsync(RequestCqrsRf3Callers caller, PartitionRef partition,
        OutboxStatus baseline, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(baseline, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.OutboxStatusAsync(partition, token)));
        await SqlRf3Protocol.EqualAsync(baseline, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await caller.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
    }

    internal static async Task SchemaAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState state, CancellationToken token)
    {
        var request = new WaitForIndexRequest(state.Scenario.Partition, state.Resource.Name,
            NativeTextMaintenanceRf3Scenario.Field, state.Receipt.Token);
        foreach (var value in new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.WaitForIndexAsync(request, token)),
            (await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(
                await caller.Mcp.CallAsync(WaitForIndexProtocol.Tool, request, token))).Value
        })
        {
            await Assert.That(value.SchemaVersion).IsEqualTo(2L);
            await Assert.That(value.AppliedToken.Incarnation).IsEqualTo(state.Receipt.Token.Incarnation);
            await Assert.That(value.AppliedToken.AtomicPartitionId).IsEqualTo(state.Scenario.Partition.AtomicPartitionId);
            await Assert.That(value.AppliedToken.OwnershipEpoch).IsEqualTo(state.Receipt.Token.OwnershipEpoch);
            await Assert.That(value.AppliedToken.Position).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        }
    }

    private static SearchRequest Request(NativeTextSelectedSchemaPublicState state, string query, bool selected)
        => new(state.Scenario.Partition, state.Resource.Name, NativeTextMaintenanceRf3Scenario.Field, query,
            TextIndex: selected ? new(state.Build.Consumer, state.Build.IndexGeneration) : null);
}
