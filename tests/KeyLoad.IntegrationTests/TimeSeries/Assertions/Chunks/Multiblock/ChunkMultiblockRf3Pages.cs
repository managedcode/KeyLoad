using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Pages
{
    private const int Limit = 258;
    private const long AbsentCut = 0;

    internal static ReadSampleChunkWindowRequest Request(ChunkMultiblockRf3State state)
        => new(state.Scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            state.Scenario.WindowId, null, null, Limit);

    internal static async Task RequireAsync(ChunkMultiblockRf3State state, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var request = Request(state);
        var call = SqlRf3Protocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        await WindowAsync(state, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleChunkWindowAsync(request, token)));
        await WindowAsync(state, (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value);
        await WindowAsync(state, await SqlRf3Protocol.SdkAsync<SampleChunkWindowResult>(sdk, call, token));
        await WindowAsync(state, await SqlRf3Protocol.McpAsync<SampleChunkWindowResult>(mcp, call, token));
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp,
            new(state.Scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                state.Scenario.From, state.Scenario.Until, Limit),
            [.. ChunkMultiblockRf3Expected.Visible(state.Expired, state.Healthy, state.Opened)], token);
    }

    internal static async Task WindowAsync(ChunkMultiblockRf3State state, SampleChunkWindowResult actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(AbsentCut);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual,
            ChunkMultiblockRf3Expected.Window(state, actual.Generation, actual.CutPosition));
    }
}
