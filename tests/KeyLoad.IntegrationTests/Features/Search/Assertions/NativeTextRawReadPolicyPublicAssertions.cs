using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRawReadPolicyPublicAssertions
{
    internal static async Task PhaseAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        NativeTextRf3Scenario scenario, bool secretVisible, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var json = secretVisible
            ? "{\"secret\":\"private-search-canary\",\"owner\":\"owner-a\"}"
            : "{\"owner\":\"owner-a\"}";
        string[] redactions = secretVisible
            ? [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField]
            : [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField, NativeTextRf3Scenario.SecretField];
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRf3Scenario.FirstId), 1L, json, true, [.. redactions]), 1d / 61d)];
        for (var repeat = 0; repeat < 2; repeat++)
        {
            foreach (var term in new[] { "needle", "Київ" })
            {
                await PageAsync(reader, scenario.Text(term), expected, token);
            }
        }
        RankedDocument[] full = [new(new(new(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRf3Scenario.FirstId), 1L,
            NativeTextRf3Scenario.Document("Київ needle needle", NativeTextRf3Scenario.OwnerA), false, []), 1d / 61d)];
        await PageAsync(administrator, scenario.Text("Київ"), full, token);
        await SqlRf3Protocol.EqualAsync(before,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
    }

    private static async Task PageAsync(RequestCqrsRf3Callers callers, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SearchAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await callers.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }
}
