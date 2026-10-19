using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextNonStringTransitionsPublicAssertions
{
    internal static async Task<OutboxStatus> CheckAsync(RequestCqrsClusterCallers caller, PartitionRef partition,
        IReadOnlyList<TextNonStringTransitionsPublicTrial.Original> originals, int stage, CancellationToken token)
    {
        await PagesAsync(caller, partition, stage, token);
        var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(partition, token));
        await ReplayAsync(caller, partition, originals, stage, baseline, token);
        return baseline;
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers caller, PartitionRef partition,
        IReadOnlyList<TextNonStringTransitionsPublicTrial.Original> originals, int stage,
        OutboxStatus baseline, CancellationToken token)
    {
        await PagesAsync(caller, partition, stage, token);
        foreach (var original in originals)
        {
            await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.CommitAsync(original.Command, token)));
            await PagesAsync(caller, partition, stage, token);
            await TextSelectedSchemaPublicAssertions.OutboxAsync(caller, partition, baseline, token);
            await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
            await PagesAsync(caller, partition, stage, token);
            await TextSelectedSchemaPublicAssertions.OutboxAsync(caller, partition, baseline, token);
        }
    }

    private static async Task PagesAsync(RequestCqrsClusterCallers caller, PartitionRef partition, int stage, CancellationToken token)
    {
        var collection = TextNonStringTransitionsPublicTrial.Collection;
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
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.SearchAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }
}
