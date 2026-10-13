using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedExplainPublicPages
{
    internal const string FirstJson = "{\"text\":\"needle\"}";
    internal const string SecondJson = "{\"text\":\"needle needle needle\"}";

    internal static async Task CurrentAsync(RequestCqrsRf3Callers caller,
        TextIndexMaintenanceRequest maintenance, bool deleted, bool selected, OutboxStatus baseline,
        CancellationToken token)
    {
        foreach (var explain in new[] { true, false })
        {
            var request = Request(maintenance, selected, explain);
            var expected = Expected(maintenance, deleted, explain);
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.SearchAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
                await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
        }
        await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, maintenance.Consumer.Partition, baseline, token);
    }

    internal static async Task StaleAsync(RequestCqrsRf3Callers caller,
        TextIndexMaintenanceRequest maintenance, OutboxStatus baseline, CancellationToken token)
    {
        var sdk = await caller.Sdk.SearchAsync(Request(maintenance, true, true), token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await Assert.That(sdk.Value).IsNull();
        await CurrentAsync(caller, maintenance, true, false, baseline, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.SearchExecute,
            Request(maintenance, true, true), token), ErrorCode.HistoryUnavailable, dispatched: true);
        await CurrentAsync(caller, maintenance, true, false, baseline, token);
    }

    private static SearchRequest Request(TextIndexMaintenanceRequest maintenance, bool selected, bool explain)
        => new(maintenance.Consumer.Partition, maintenance.Collection, maintenance.Field, "needle",
            TextWeight: 2d, FusionConstant: 10, Explain: explain,
            TextIndex: selected ? new(maintenance.Consumer, maintenance.IndexGeneration) : null);

    private static RankedDocument[] Expected(TextIndexMaintenanceRequest maintenance, bool deleted, bool explain)
    {
        var partition = maintenance.Consumer.Partition;
        var first = new DocumentResult(new(partition, maintenance.Collection, "a"), 1L, FirstJson, false, []);
        if (deleted)
        { return [new(first, 2d / 11d, explain ? new(10, [new(SearchBranchKind.Text, 1, 2d, 2d / 11d)]) : null)]; }
        var second = new DocumentResult(new(partition, maintenance.Collection, "b"), 1L, SecondJson, false, []);
        return [new(second, 2d / 11d, explain ? new(10, [new(SearchBranchKind.Text, 1, 2d, 2d / 11d)]) : null),
            new(first, 2d / 12d, explain ? new(10, [new(SearchBranchKind.Text, 2, 2d, 2d / 12d)]) : null)];
    }
}
