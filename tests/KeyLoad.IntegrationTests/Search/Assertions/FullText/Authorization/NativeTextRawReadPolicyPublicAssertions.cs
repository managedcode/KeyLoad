using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRawReadPolicyPublicAssertions
{
    internal static async Task PhaseAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        NativeTextClusterScenario scenario, bool secretVisible, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var json = secretVisible
            ? "{\"secret\":\"private-search-canary\",\"owner\":\"owner-a\"}"
            : "{\"owner\":\"owner-a\"}";
        string[] redactions = secretVisible
            ? [NativeTextClusterScenario.TextField, NativeTextClusterScenario.VectorField]
            : [NativeTextClusterScenario.TextField, NativeTextClusterScenario.VectorField, NativeTextClusterScenario.SecretField];
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextClusterScenario.Collection,
            NativeTextClusterScenario.FirstId), 1L, json, true, [.. redactions]), 1d / 61d)];
        for (var repeat = 0; repeat < 2; repeat++)
        {
            foreach (var term in new[] { "needle", "Київ" })
            {
                await PageAsync(reader, scenario.Text(term), expected, token);
            }
        }
        RankedDocument[] full = [new(new(new(scenario.Partition, NativeTextClusterScenario.Collection,
            NativeTextClusterScenario.FirstId), 1L,
            NativeTextClusterScenario.Document("Київ needle needle", NativeTextClusterScenario.OwnerA), false, []), 1d / 61d)];
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
