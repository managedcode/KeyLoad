using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockClusterPages
{
    private const int Limit = 258;
    private const long AbsentCut = 0;

    internal static ReadSampleChunkWindowRequest Request(ChunkMultiblockClusterState state)
        => new(state.Scenario.Execution.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
            state.Scenario.WindowId, null, null, Limit);

    internal static async Task RequireAsync(ChunkMultiblockClusterState state, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var request = Request(state);
        var call = SqlClusterProtocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        await WindowAsync(state, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleChunkWindowAsync(request, token)));
        await WindowAsync(state, (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value);
        await WindowAsync(state, await SqlClusterProtocol.SdkAsync<SampleChunkWindowResult>(sdk, call, token));
        await WindowAsync(state, await SqlClusterProtocol.McpAsync<SampleChunkWindowResult>(mcp, call, token));
        await SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp,
            new(state.Scenario.Execution.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                state.Scenario.From, state.Scenario.Until, Limit),
            [.. ChunkMultiblockClusterExpected.Visible(state.Expired, state.Healthy, state.Opened)], token);
    }

    internal static async Task WindowAsync(ChunkMultiblockClusterState state, SampleChunkWindowResult actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(AbsentCut);
        await SampleChunkRetentionClusterReadOracle.EqualAsync(actual,
            ChunkMultiblockClusterExpected.Window(state, actual.Generation, actual.CutPosition));
    }
}
