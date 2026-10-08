using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnPublicRf3Assertions
{
    private const int Version = 1;
    private const long Revision = 1;
    private const long EmptyPosition = 0;
    private const long MissingGeneration = 2;
    private const string Tool = "keyload_search_ann_read";
    private static readonly double[] Scores = [1d / 61d, 1d / 62d, 1d / 63d];

    internal static async Task AllPathsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeAnnMaintenanceRf3Scenario scenario, AnnMaintenanceRequest pin, bool empty, CancellationToken token)
    {
        var request = new ApproximateSearchRequest(Version, scenario.Search(), pin.Consumer, pin.IndexGeneration);
        var missing = request with { IndexGeneration = MissingGeneration };
        var denied = await sdk.ApproximateSearchAsync(missing, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.HistoryUnavailable.ToString());
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(Tool, missing, token), ErrorCode.HistoryUnavailable, dispatched: true);
        if (empty)
        { request = request with { Search = request.Search with { AllowedIds = [] } }; }
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.ApproximateSearchAsync(request, token));
        await PageAsync(direct, scenario, pin, empty);
        var official = (await McpCallerAssertions.SuccessAsync<AnnSearchPage>(await mcp.CallAsync(Tool, request, token))).Value;
        var sql = SqlRf3Protocol.Call(scenario.Partition, Tool, request);
        var sdkCall = await SqlRf3Protocol.SdkAsync<AnnSearchPage>(sdk, sql, token);
        var mcpCall = await SqlRf3Protocol.McpAsync<AnnSearchPage>(mcp, sql, token);
        await EqualAsync(official, direct);
        await EqualAsync(sdkCall, direct);
        await EqualAsync(mcpCall, direct);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.ApproximateSearchAsync(
            request with { Search = scenario.Search() }, token));
        await PageAsync(healthy, scenario, pin, empty: false);
    }

    private static async Task EqualAsync(AnnSearchPage actual, AnnSearchPage expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    private static async Task PageAsync(AnnSearchPage actual, NativeAnnMaintenanceRf3Scenario scenario,
        AnnMaintenanceRequest pin, bool empty)
    {
        await Assert.That(actual.Position).IsGreaterThan(EmptyPosition);
        var rows = empty ? [] : NativeAnnMaintenanceRf3Scenario.Ids.Select((id, index) =>
            new RankedDocument(new(new(scenario.Partition, NativeAnnMaintenanceRf3Scenario.Collection, id),
                Revision, NativeAnnMaintenanceRf3Scenario.Json, false, []), Scores[index])).ToArray();
        var expected = new AnnSearchPage(Version, [.. rows], actual.Position, AnnPageMode.Exact, true,
            pin.IndexGeneration, AnnPageMode.Approximate);
        await EqualAsync(actual, expected);
    }
}
