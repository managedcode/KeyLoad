using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextWaitRf3Assertions
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
        await NativeTextWaitRf3CutAssertions.RunAsync(NativeTextWaitRf3CutAssertions.Sdk,
            async () => await McpCallerAssertions.SdkSuccessAsync(await sdk.WaitForIndexAsync(request, token)),
            administrator, request, schemaVersion, policyEpoch, token);
        await NativeTextWaitRf3CutAssertions.RunAsync(NativeTextWaitRf3CutAssertions.Mcp,
            async () => (await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(
                await official.CallAsync(WaitForIndexProtocol.Tool, request, token))).Value,
            administrator, request, schemaVersion, policyEpoch, token);
        var call = SqlRf3Protocol.Call(request.Partition, WaitForIndexProtocol.Tool, request);
        await NativeTextWaitRf3CutAssertions.RunAsync(NativeTextWaitRf3CutAssertions.SdkSql,
            () => SqlRf3Protocol.SdkAsync<WaitForIndexResult>(sdk, call, token),
            administrator, request, schemaVersion, policyEpoch, token);
        await NativeTextWaitRf3CutAssertions.RunAsync(NativeTextWaitRf3CutAssertions.McpSql,
            () => SqlRf3Protocol.McpAsync<WaitForIndexResult>(official, call, token),
            administrator, request, schemaVersion, policyEpoch, token);
    }

    internal static async Task LiteralAsync(NativeTextRf3Scenario scenario, KeyLoadClient sdk, McpOfficialClient official,
        bool deleted, CancellationToken token)
    {
        var first = new RankedDocument(new(new(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRf3Scenario.FirstId), Revision, FirstJson, true, [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField, NativeTextRf3Scenario.SecretField]), 1d / FirstDenominator);
        var second = new RankedDocument(new(new(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRf3Scenario.SecondId), Revision, SecondJson, true, [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField, NativeTextRf3Scenario.SecretField]), 1d / SecondDenominator);
        RankedDocument[] expected = deleted ? [first] : [first, second];
        var request = scenario.Text(Query);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token));
        var mcp = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await official.CallAsync(McpCallerTools.SearchExecute, request, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, mcp.Value);
        var sql = SqlRf3Protocol.Call(request.Partition, McpCallerTools.SearchExecute, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<RankedDocument[]>(sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<RankedDocument[]>(official, sql, token));
    }
}
