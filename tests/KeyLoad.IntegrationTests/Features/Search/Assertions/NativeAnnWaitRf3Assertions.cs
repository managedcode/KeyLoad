using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnWaitRf3Assertions
{
    private const long CurrentVersion = 1;
    private const int PageVersion = 1;
    private const int FirstDenominator = 61;
    private const int SecondDenominator = 62;
    private const int ThirdDenominator = 63;
    private const string FirstJson = "{\"owner\":\"owner-a\"}";
    private const string SecondJson = "{\"owner\":\"owner-b\"}";
    private const string ThirdJson = "{\"owner\":\"owner-b\"}";

    internal static async Task RejectedAsync(KeyLoadClient administrator, RequestCqrsRf3Callers caller,
        WaitForAnnIndexRequest request, ErrorCode expected, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        var denied = await caller.Sdk.WaitForAnnIndexAsync(request, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        _ = await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(WaitForAnnIndexProtocol.Tool, request, token), expected, dispatched: true);
        var sql = SqlRf3Protocol.Call(request.Partition, WaitForAnnIndexProtocol.Tool, request);
        var sqlDenied = await caller.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlDenied.IsSuccess).IsFalse();
        await Assert.That(sqlDenied.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(sqlDenied.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        _ = await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), expected, dispatched: true);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
    }

    internal static async Task AllPathsAsync(RequestCqrsRf3Callers caller, WaitForAnnIndexRequest request,
        long indexed, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.WaitForAnnIndexAsync(request, token));
        await Assert.That(direct.IndexedToken).IsEqualTo(request.MinimumToken with { Position = indexed });
        await Assert.That(direct.IndexedToken.Position).IsGreaterThanOrEqualTo(request.MinimumToken.Position);
        await Assert.That(direct.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(direct.SchemaVersion).IsEqualTo(CurrentVersion);
        await Assert.That(direct.PolicyEpoch).IsEqualTo(CurrentVersion);
        var official = (await McpCallerAssertions.SuccessAsync<WaitForAnnIndexResult>(await caller.Mcp.CallAsync(WaitForAnnIndexProtocol.Tool, request, token))).Value;
        await SqlRf3Protocol.EqualAsync(direct, official);
        var sql = SqlRf3Protocol.Call(request.Partition, WaitForAnnIndexProtocol.Tool, request);
        await SqlRf3Protocol.EqualAsync(direct, await SqlRf3Protocol.SdkAsync<WaitForAnnIndexResult>(caller.Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(direct, await SqlRf3Protocol.McpAsync<WaitForAnnIndexResult>(caller.Mcp, sql, token));
    }

    internal static async Task LiteralAsync(RequestCqrsRf3Callers caller, NativeTextRf3Scenario scenario,
        AnnMaintenanceRequest pin, bool deleted, CancellationToken token)
    {
        RankedDocument[] rows = deleted
            ? [Row(scenario, NativeTextRf3Scenario.FirstId, FirstJson, FirstDenominator), Row(scenario, NativeTextRf3Scenario.SecondId, SecondJson, SecondDenominator)]
            : [Row(scenario, NativeTextRf3Scenario.FirstId, FirstJson, FirstDenominator), Row(scenario, NativeTextRf3Scenario.SecondId, SecondJson, SecondDenominator),
                Row(scenario, NativeTextRf3Scenario.ThirdId, ThirdJson, ThirdDenominator)];
        var request = new ApproximateSearchRequest(PageVersion, scenario.Hybrid() with { TextField = null, Text = null }, pin.Consumer, pin.IndexGeneration);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ApproximateSearchAsync(request, token));
        var expected = new AnnSearchPage(PageVersion, [.. rows], actual.Position, AnnPageMode.Exact, true, pin.IndexGeneration, AnnPageMode.Approximate);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        var official = (await McpCallerAssertions.SuccessAsync<AnnSearchPage>(await caller.Mcp.CallAsync(AnnSearchProtocol.Tool, request, token))).Value;
        await SqlRf3Protocol.EqualAsync(expected, official);
        var sql = SqlRf3Protocol.Call(scenario.Partition, AnnSearchProtocol.Tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<AnnSearchPage>(caller.Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<AnnSearchPage>(caller.Mcp, sql, token));
    }

    private static RankedDocument Row(NativeTextRf3Scenario scenario, string id, string json, int denominator)
        => new(new(new(scenario.Partition, NativeTextRf3Scenario.Collection, id), CurrentVersion, json, true, [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField, NativeTextRf3Scenario.SecretField]), 1d / denominator);
}
