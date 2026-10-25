using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class TextDistinctTermsPublicAssertions
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers caller, TextClusterScenario scenario,
        CommitToken minimum, int stage, CancellationToken token)
    {
        var wait = new WaitForIndexRequest(scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.TextField, minimum);
        foreach (var result in new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.WaitForIndexAsync(wait, token)),
            (await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(
                await caller.Mcp.CallAsync(WaitForIndexProtocol.Tool, wait, token))).Value
        })
        {
            await Assert.That(result.AppliedToken.Incarnation).IsEqualTo(minimum.Incarnation);
            await Assert.That(result.AppliedToken.AtomicPartitionId).IsEqualTo(minimum.AtomicPartitionId);
            await Assert.That(result.AppliedToken.Position).IsGreaterThanOrEqualTo(minimum.Position);
            await Assert.That(result.SchemaVersion).IsEqualTo(scenario.Resource.SchemaVersion);
        }
        RankedDocument[] expected = stage switch
        {
            0 => [Row(scenario, TextDistinctTermsPublicScenario.Second, 1L,
                    TextDistinctTermsPublicScenario.SecondJson, 1d / 61d),
                  Row(scenario, TextDistinctTermsPublicScenario.First, 1L,
                    TextDistinctTermsPublicScenario.FirstJson, 1d / 62d),
                  Row(scenario, TextDistinctTermsPublicScenario.Plain, 1L,
                    TextDistinctTermsPublicScenario.PlainJson, 1d / 63d)],
            1 => [Row(scenario, TextDistinctTermsPublicScenario.First, 2L,
                    TextDistinctTermsPublicScenario.ChangedJson, 1d / 61d),
                  Row(scenario, TextDistinctTermsPublicScenario.Second, 1L,
                    TextDistinctTermsPublicScenario.SecondJson, 1d / 62d),
                  Row(scenario, TextDistinctTermsPublicScenario.Plain, 1L,
                    TextDistinctTermsPublicScenario.PlainJson, 1d / 63d)],
            2 => [Row(scenario, TextDistinctTermsPublicScenario.First, 2L,
                    TextDistinctTermsPublicScenario.ChangedJson, 1d / 61d),
                  Row(scenario, TextDistinctTermsPublicScenario.Plain, 1L,
                    TextDistinctTermsPublicScenario.PlainJson, 1d / 62d)],
            3 => [Row(scenario, TextDistinctTermsPublicScenario.First, 3L,
                    TextDistinctTermsPublicScenario.HealthyJson, 1d / 61d),
                  Row(scenario, TextDistinctTermsPublicScenario.Plain, 1L,
                    TextDistinctTermsPublicScenario.PlainJson, 1d / 62d)],
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        foreach (var query in new[] { "rare common", "common rare" })
        {
            await PageAsync(caller, scenario.Text(query), expected, token);
        }
    }

    private static RankedDocument Row(TextClusterScenario scenario, string id, long revision, string json, double score)
        => new(new(scenario.StoredReference(id), revision, json, false, []), score, null);

    private static async Task PageAsync(RequestCqrsClusterCallers caller, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.SearchAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task OutboxAsync(RequestCqrsClusterCallers caller, PartitionRef partition,
        OutboxStatus baseline, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(baseline, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(partition, token)));
        await SqlClusterProtocol.EqualAsync(baseline, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await caller.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
    }
}
