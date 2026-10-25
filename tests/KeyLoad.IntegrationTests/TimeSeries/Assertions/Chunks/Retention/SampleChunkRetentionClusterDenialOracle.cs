using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkRetentionClusterDenialOracle
{
    internal static async Task WindowAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleChunkWindowRequest request, CancellationToken token)
    {
        var direct = await sdk.ReadSampleChunkWindowAsync(request, token).ConfigureAwait(false);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token)
            .ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true)).Value;
        await SqlAsync(sdk, mcp, SqlClusterProtocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request), token);
    }

    internal static async Task RollupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleRollupRequest request, CancellationToken token)
    {
        var direct = await sdk.ReadSampleRollupAsync(request, token).ConfigureAwait(false);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SampleRollupProtocol.ReadTool, request, token)
            .ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true)).Value;
        await SqlAsync(sdk, mcp, SqlClusterProtocol.Call(request.Partition, SampleRollupProtocol.ReadTool, request), token);
    }

    private static async Task SqlAsync(KeyLoadClient sdk, McpOfficialClient mcp, SqlOperationRequest request,
        CancellationToken token)
    {
        var direct = await sdk.ExecuteSqlAsync(request, token).ConfigureAwait(false);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, request, token)
            .ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true)).Value;
    }
}
