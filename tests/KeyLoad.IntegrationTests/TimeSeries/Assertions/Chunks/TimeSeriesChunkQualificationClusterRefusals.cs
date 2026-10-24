using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class TimeSeriesChunkQualificationClusterRefusals
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, TimeSeriesChunkQualificationClusterState state,
        bool scopeDenied, CancellationToken token)
    {
        foreach (var partition in state.Partitions)
        {
            var request = TimeSeriesChunkQualificationClusterData.Request(partition, state.Window) with
            {
                TagPointer = TimeSeriesChunkQualificationClusterData.PrivatePath,
                TagValue = "alpha"
            };
            await ChunkAsync(reader, request, ErrorCode.PermissionDenied, state.Reader.Secret,
                scopeDenied ? TimeSeriesChunkQualificationClusterData.ScopeDenied : TimeSeriesChunkQualificationClusterData.FieldUseDenied,
                scopeDenied ? TimeSeriesChunkQualificationClusterData.ScopeDenied : TimeSeriesChunkQualificationClusterData.SafePermissionDenied, token);
        }
    }
    internal static async Task ChunkAsync(RequestCqrsClusterCallers callers,
        ReadSampleChunkWindowRequest request, ErrorCode code, string credential, string detail, string safe, CancellationToken token)
    {
        var sdk = await callers.Sdk.ReadSampleChunkWindowAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), sdk.Problem);
        await ProblemAsync(await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token), credential, code, safe);
        var call = SqlClusterProtocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        var sql = await callers.Sdk.ExecuteSqlAsync(call, token);
        await Assert.That(sql.IsFailed).IsTrue();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), sql.Problem);
        await ProblemAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, call, token), credential, code, safe);
    }
    private static async Task ProblemAsync(CallToolResult reply, string credential, ErrorCode code, string detail)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, "alpha");
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, "beta");
    }
    internal static Task BoundAsync(RequestCqrsClusterCallers reader, TimeSeriesChunkQualificationClusterState state, CancellationToken token)
    {
        var request = TimeSeriesChunkQualificationClusterData.Request(state.Partitions[TimeSeriesChunkQualificationClusterData.SiblingScope], state.Window) with
        {
            Limit = TimeSeriesChunkQualificationClusterData.RefusedReadLimit,
            MinimumToken = state.Originals[TimeSeriesChunkQualificationClusterData.SiblingScope].Receipt.Token
        };
        return ChunkAsync(reader, request, ErrorCode.BudgetExceeded, state.Reader.Secret,
            TimeSeriesChunkQualificationClusterData.ReadBoundExceeded, TimeSeriesChunkQualificationClusterData.SafeBoundExceeded, token);
    }
    internal static async Task CancellationAsync(RequestCqrsClusterCallers reader, TimeSeriesChunkQualificationClusterState state)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var originalToken = cancellation.Token;
        var request = TimeSeriesChunkQualificationClusterData.Request(state.Partitions[TimeSeriesChunkQualificationClusterData.SiblingScope], state.Window);
        var original = reader.Sdk.ReadSampleChunkWindowAsync(request, originalToken);
        var result = await original;
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, TimeSeriesChunkQualificationClusterData.ReadUnavailable), result.Problem);
        var official = reader.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, originalToken);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await official);
        await Assert.That(failure!.CancellationToken).IsEqualTo(originalToken);
    }
}
