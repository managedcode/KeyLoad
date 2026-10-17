using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesRetentionAtomicPublicRefusals
{
    internal static async Task AllAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        SeriesRetentionAtomicPublicState state, CancellationToken token)
    {
        await RefusedAsync(administrator, reader, state, state.ConflictSdk, ErrorCode.Conflict,
            SeriesRetentionAtomicPublicExpected.ConflictDetail, false, token);
        await RefusedAsync(administrator, reader, state, state.ConflictMcp, ErrorCode.Conflict,
            SeriesRetentionAtomicPublicExpected.ConflictDetail, true, token);
        await RefusedAsync(administrator, reader, state, state.MixedSdk, ErrorCode.HistoryUnavailable,
            SeriesRetentionAtomicPublicExpected.FloorDetail, false, token);
        await RefusedAsync(administrator, reader, state, state.MixedMcp, ErrorCode.HistoryUnavailable,
            SeriesRetentionAtomicPublicExpected.FloorDetail, true, token);
    }

    private static async Task RefusedAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        SeriesRetentionAtomicPublicState state, CommandRequest command, ErrorCode error, string detail,
        bool mcpFirst, CancellationToken token)
    {
        if (mcpFirst)
        {
            await McpAsync(administrator, command, error, token);
            await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
            await SdkAsync(administrator, command, error, detail, token);
        }
        else
        {
            await SdkAsync(administrator, command, error, detail, token);
            await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
            await McpAsync(administrator, command, error, token);
        }
        await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
    }

    private static async Task SdkAsync(RequestCqrsRf3Callers administrator, CommandRequest command,
        ErrorCode error, string detail, CancellationToken token)
    {
        var reply = await administrator.Sdk.CommitAsync(command, token);
        await Assert.That(reply.IsFailed).IsTrue();
        await Assert.That(reply.Value).IsNull();
        await Assert.That(reply.Problem).IsNotNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(error, detail), reply.Problem!);
    }

    private static async Task McpAsync(RequestCqrsRf3Callers administrator, CommandRequest command,
        ErrorCode error, CancellationToken token)
    {
        var reply = await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(reply, error, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SeriesRetentionAtomicPublicExpected.McpDetail);
    }
}
