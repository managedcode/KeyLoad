using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextNonStringTransitionsPublicAssertions
{
    internal static async Task<OutboxStatus> CheckAsync(RequestCqrsRf3Callers caller, PartitionRef partition,
        IReadOnlyList<NativeTextNonStringTransitionsPublicTrial.Original> originals, int stage, CancellationToken token)
    {
        await PagesAsync(caller, partition, stage, token);
        var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(partition, token));
        await ReplayAsync(caller, partition, originals, stage, baseline, token);
        return baseline;
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller, PartitionRef partition,
        IReadOnlyList<NativeTextNonStringTransitionsPublicTrial.Original> originals, int stage,
        OutboxStatus baseline, CancellationToken token)
    {
        await PagesAsync(caller, partition, stage, token);
        foreach (var original in originals)
        {
            await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.CommitAsync(original.Command, token)));
            await PagesAsync(caller, partition, stage, token);
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, partition, baseline, token);
            await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
            await PagesAsync(caller, partition, stage, token);
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, partition, baseline, token);
        }
    }

    private static async Task PagesAsync(RequestCqrsRf3Callers caller, PartitionRef partition, int stage, CancellationToken token)
    {
        var collection = NativeTextNonStringTransitionsPublicTrial.Collection;
        RankedDocument Row(string id, long revision, string json, double score)
            => new(new(new(partition, collection, id), revision, json, false, []), score, null);
        RankedDocument[] expected = stage switch
        {
            0 => [Row("a", 1L, "{\"text\":\"needle\"}", 1d / 61d)],
            1 => [Row("b", 2L, "{\"text\":\"needle needle\"}", 1d / 61d),
                  Row("a", 1L, "{\"text\":\"needle\"}", 1d / 62d)],
            2 => [Row("b", 2L, "{\"text\":\"needle needle\"}", 1d / 61d)],
            3 => [],
            _ => throw new InvalidOperationException("The text scalar fixture stage is invalid.")
        };
        var request = new SearchRequest(partition, collection, "/text", "needle");
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.SearchAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }
}
