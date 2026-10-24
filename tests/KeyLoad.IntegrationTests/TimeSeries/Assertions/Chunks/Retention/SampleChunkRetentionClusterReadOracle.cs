using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkRetentionClusterReadOracle
{
    internal static async Task WindowAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleChunkWindowRequest request, SampleChunkWindowResult expected, CommitReceipt minimum, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleChunkWindowAsync(request, token).ConfigureAwait(false));
        var official = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token).ConfigureAwait(false))).Value;
        var call = SqlClusterProtocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        var sql = await SqlClusterProtocol.SdkAsync<SampleChunkWindowResult>(sdk, call, token).ConfigureAwait(false);
        var sqlOfficial = await SqlClusterProtocol.McpAsync<SampleChunkWindowResult>(mcp, call, token).ConfigureAwait(false);
        foreach (var actual in new[] { direct, official, sql, sqlOfficial })
        {
            await Assert.That(actual.CutPosition >= minimum.Token.Position).IsTrue();
            await EqualAsync(actual, expected with
            {
                CutPosition = actual.CutPosition
            });
        }
    }

    internal static async Task RollupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleRollupRequest request, SampleRollupResult expected, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleRollupAsync(request, token).ConfigureAwait(false));
        var official = (await McpCallerAssertions.SuccessAsync<SampleRollupResult>(
            await mcp.CallAsync(SampleRollupProtocol.ReadTool, request, token).ConfigureAwait(false))).Value;
        var call = SqlClusterProtocol.Call(request.Partition, SampleRollupProtocol.ReadTool, request);
        var sql = await SqlClusterProtocol.SdkAsync<SampleRollupResult>(sdk, call, token).ConfigureAwait(false);
        var sqlOfficial = await SqlClusterProtocol.McpAsync<SampleRollupResult>(mcp, call, token).ConfigureAwait(false);
        foreach (var actual in new[] { direct, official, sql, sqlOfficial })
        {
            await EqualAsync(actual, expected);
        }
    }

    internal static async Task RawAsync(KeyLoadClient sdk, McpOfficialClient mcp, ReadSamplesRequest request,
        SampleRecord[] expected, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(request, token).ConfigureAwait(false));
        var official = (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await mcp.CallAsync(McpCallerTools.SeriesRead, request, token).ConfigureAwait(false))).Value;
        var call = SqlClusterProtocol.Call(request.Partition, McpCallerTools.SeriesRead, request);
        var sql = await SqlClusterProtocol.SdkAsync<SampleRecord[]>(sdk, call, token).ConfigureAwait(false);
        var sqlOfficial = await SqlClusterProtocol.McpAsync<SampleRecord[]>(mcp, call, token).ConfigureAwait(false);
        foreach (var actual in new[] { direct, official, sql, sqlOfficial })
        {
            await EqualAsync(actual, expected);
        }
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
}
