using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextWaitClusterAssertions
{
    private const string FirstJson = "{\"owner\":\"owner-a\"}";
    private const string SecondJson = "{\"owner\":\"owner-b\"}";
    private const string Query = "needle";
    private const long Revision = 1;
    private const int FirstDenominator = 61;
    private const int SecondDenominator = 62;

    internal static async Task WaitAsync(KeyLoadClient sdk, McpOfficialClient official, KeyLoadClient administrator,
        WaitForIndexRequest request, long schemaVersion, long policyEpoch, CancellationToken token)
    {
        await TextWaitClusterCutAssertions.RunAsync(TextWaitClusterCutAssertions.Sdk,
            async () => await TextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.WaitForIndexAsync(request, token)),
            administrator, request, schemaVersion, policyEpoch, token);
        await TextWaitClusterCutAssertions.RunAsync(TextWaitClusterCutAssertions.Mcp,
            async () => (await TextPublicOutcomeAssertions.SuccessAsync<WaitForIndexResult>(
                await official.CallAsync(WaitForIndexProtocol.Tool, request, token))).Value,
            administrator, request, schemaVersion, policyEpoch, token);
        var call = SqlClusterProtocol.Call(request.Partition, WaitForIndexProtocol.Tool, request);
        await TextWaitClusterCutAssertions.RunAsync(TextWaitClusterCutAssertions.SdkSql,
            () => SqlClusterProtocol.SdkAsync<WaitForIndexResult>(sdk, call, token),
            administrator, request, schemaVersion, policyEpoch, token);
        await TextWaitClusterCutAssertions.RunAsync(TextWaitClusterCutAssertions.McpSql,
            () => SqlClusterProtocol.McpAsync<WaitForIndexResult>(official, call, token),
            administrator, request, schemaVersion, policyEpoch, token);
    }

    internal static async Task LiteralAsync(TextClusterScenario scenario, KeyLoadClient sdk, McpOfficialClient official,
        bool deleted, CancellationToken token)
    {
        var first = new RankedDocument(new(new(scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.FirstId), Revision, FirstJson, true, [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField]), 1d / FirstDenominator);
        var second = new RankedDocument(new(new(scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.SecondId), Revision, SecondJson, true, [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField]), 1d / SecondDenominator);
        RankedDocument[] expected = deleted ? [first] : [first, second];
        var request = scenario.Text(Query);
        var actual = await TextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token));
        var mcp = await TextPublicOutcomeAssertions.SuccessAsync<RankedDocument[]>(await official.CallAsync(McpCallerTools.SearchExecute, request, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, mcp.Value);
        var sql = SqlClusterProtocol.Call(request.Partition, McpCallerTools.SearchExecute, request);
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.SdkAsync<RankedDocument[]>(sdk, sql, token));
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.McpAsync<RankedDocument[]>(official, sql, token));
    }
}
