using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3DenialOracle
{
    internal static async Task WindowAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleChunkWindowRequest request, CancellationToken token)
    {
        var direct = await sdk.ReadSampleChunkWindowAsync(request, token).ConfigureAwait(false);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token)
            .ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true);
        await SqlAsync(sdk, mcp, SqlRf3Protocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request), token);
    }

    internal static async Task RollupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleRollupRequest request, CancellationToken token)
    {
        var direct = await sdk.ReadSampleRollupAsync(request, token).ConfigureAwait(false);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SampleRollupProtocol.ReadTool, request, token)
            .ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true);
        await SqlAsync(sdk, mcp, SqlRf3Protocol.Call(request.Partition, SampleRollupProtocol.ReadTool, request), token);
    }

    private static async Task SqlAsync(KeyLoadClient sdk, McpOfficialClient mcp, SqlOperationRequest request,
        CancellationToken token)
    {
        var direct = await sdk.ExecuteSqlAsync(request, token).ConfigureAwait(false);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, request, token)
            .ConfigureAwait(false), ErrorCode.HistoryUnavailable, dispatched: true);
    }
}
