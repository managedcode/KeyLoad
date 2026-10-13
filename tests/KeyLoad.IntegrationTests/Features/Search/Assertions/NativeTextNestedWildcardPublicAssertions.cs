using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextNestedWildcardPublicAssertions
{
    internal static async Task PhaseAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        NativeTextNestedWildcardPublicScenario state, int visibility, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Partition, token));
        var json = visibility == 0 ? NativeTextNestedWildcardPublicScenario.Hidden : visibility == 1
            ? NativeTextNestedWildcardPublicScenario.ProfileVisible : NativeTextNestedWildcardPublicScenario.Full;
        string[] hidden = visibility == 0 ? [NativeTextNestedWildcardPublicScenario.ProfilePolicy,
            NativeTextNestedWildcardPublicScenario.ArrayPolicy] : visibility == 1
            ? [NativeTextNestedWildcardPublicScenario.ArrayPolicy] : [];
        await PageAsync(reader, state, json, hidden, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(state.Original, token)));
        await PageAsync(reader, state, json, hidden, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original, token))).Value);
        await PageAsync(reader, state, json, hidden, token);
        await PageAsync(administrator, state, NativeTextNestedWildcardPublicScenario.Full, [], token);
        await OutboxAsync(administrator, state.Partition, before, token);
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        NativeTextNestedWildcardPublicScenario state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Partition, token));
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var denied = await reader.Sdk.SearchAsync(state.Request(), token);
            await Assert.That(denied.IsFailed).IsTrue();
            await Assert.That(denied.Value).IsNull();
            await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SearchExecute,
                state.Request(), token), ErrorCode.PermissionDenied, dispatched: true);
            await PageAsync(administrator, state, NativeTextNestedWildcardPublicScenario.Full, [], token);
            await OutboxAsync(administrator, state.Partition, before, token);
        }
    }

    private static async Task PageAsync(RequestCqrsRf3Callers callers, NativeTextNestedWildcardPublicScenario state,
        string json, string[] hidden, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(state.Partition, NativeTextNestedWildcardPublicScenario.Collection, "a"),
            1L, json, hidden.Length != 0, [.. hidden]), 1d / 61d)];
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.SearchAsync(state.Request(), token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await callers.Mcp.CallAsync(McpCallerTools.SearchExecute, state.Request(), token))).Value);
    }

    private static async Task OutboxAsync(RequestCqrsRf3Callers administrator, PartitionRef partition,
        OutboxStatus expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(partition, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
    }
}
