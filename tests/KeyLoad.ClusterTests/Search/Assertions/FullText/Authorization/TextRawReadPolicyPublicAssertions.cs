using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class TextRawReadPolicyPublicAssertions
{
    internal static async Task PhaseAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TextClusterScenario scenario, bool secretVisible, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var json = secretVisible
            ? "{\"secret\":\"private-search-canary\",\"owner\":\"owner-a\"}"
            : "{\"owner\":\"owner-a\"}";
        string[] redactions = secretVisible
            ? [TextClusterScenario.TextField, TextClusterScenario.VectorField]
            : [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField];
        RankedDocument[] expected = [new(new(scenario.StoredReference(TextClusterScenario.FirstId), 1L, json, true, [.. redactions]), 1d / 61d)];
        for (var repeat = 0; repeat < 2; repeat++)
        {
            foreach (var term in new[] { "needle", "Київ" })
            {
                await PageAsync(reader, scenario.Text(term), expected, token);
            }
        }
        RankedDocument[] full = [new(new(scenario.StoredReference(TextClusterScenario.FirstId), 1L,
            TextClusterScenario.Document("Київ needle needle", TextClusterScenario.OwnerA), false, []), 1d / 61d)];
        await PageAsync(administrator, scenario.Text("Київ"), full, token);
        await SqlClusterProtocol.EqualAsync(before,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
    }

    private static async Task PageAsync(RequestCqrsClusterCallers callers, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SearchAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await callers.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }
}
