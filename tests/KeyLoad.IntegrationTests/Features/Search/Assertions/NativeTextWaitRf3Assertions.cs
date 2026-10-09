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

    internal static async Task WaitAsync(KeyLoadClient sdk, McpOfficialClient official, WaitForIndexRequest request,
        CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.WaitForIndexAsync(request, token));
        await Assert.That(direct.AppliedToken.Incarnation).IsEqualTo(request.MinimumToken.Incarnation);
        await Assert.That(direct.AppliedToken.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(direct.AppliedToken.OwnershipEpoch).IsEqualTo(request.MinimumToken.OwnershipEpoch);
        await Assert.That(direct.AppliedToken.Position).IsGreaterThanOrEqualTo(request.MinimumToken.Position);
        var mcp = await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(await official.CallAsync(WaitForIndexProtocol.Tool, request, token));
        await SqlRf3Protocol.EqualAsync(direct, mcp.Value);
        var call = SqlRf3Protocol.Call(request.Partition, WaitForIndexProtocol.Tool, request);
        await SqlRf3Protocol.EqualAsync(direct, await SqlRf3Protocol.SdkAsync<WaitForIndexResult>(sdk, call, token));
        await SqlRf3Protocol.EqualAsync(direct, await SqlRf3Protocol.McpAsync<WaitForIndexResult>(official, call, token));
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
