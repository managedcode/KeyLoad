using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class TextNestedWildcardPublicAssertions
{
    internal static async Task PhaseAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TextNestedWildcardPublicScenario state, int visibility, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Partition, token));
        var json = visibility == 0 ? TextNestedWildcardPublicScenario.Hidden : visibility == 1
            ? TextNestedWildcardPublicScenario.ProfileVisible : TextNestedWildcardPublicScenario.Full;
        string[] hidden = visibility == 0 ? [TextNestedWildcardPublicScenario.ProfilePolicy,
            TextNestedWildcardPublicScenario.ArrayPolicy] : visibility == 1
            ? [TextNestedWildcardPublicScenario.ArrayPolicy] : [];
        await PageAsync(reader, state, json, hidden, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(state.Original, token)));
        await PageAsync(reader, state, json, hidden, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original, token))).Value);
        await PageAsync(reader, state, json, hidden, token);
        await PageAsync(administrator, state, TextNestedWildcardPublicScenario.Full, [], token);
        await OutboxAsync(administrator, state.Partition, before, token);
    }

    internal static async Task DeniedAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TextNestedWildcardPublicScenario state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Partition, token));
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var denied = await reader.Sdk.SearchAsync(state.Request(), token);
            await Assert.That(denied.IsFailed).IsTrue();
            await Assert.That(denied.Value).IsNull();
            await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SearchExecute,
                state.Request(), token), ErrorCode.PermissionDenied, dispatched: true)).Value;
            await PageAsync(administrator, state, TextNestedWildcardPublicScenario.Full, [], token);
            await OutboxAsync(administrator, state.Partition, before, token);
        }
    }

    private static async Task PageAsync(RequestCqrsClusterCallers callers, TextNestedWildcardPublicScenario state,
        string json, string[] hidden, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(state.StoredReference("a"),
            1L, json, hidden.Length != 0, [.. hidden]), 1d / 61d)];
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.SearchAsync(state.Request(), token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await callers.Mcp.CallAsync(McpCallerTools.SearchExecute, state.Request(), token))).Value);
    }

    private static async Task OutboxAsync(RequestCqrsClusterCallers administrator, PartitionRef partition,
        OutboxStatus expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(partition, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
    }
}
