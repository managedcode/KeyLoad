using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationRf3Refusals
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, TimeSeriesChunkQualificationRf3State state,
        bool scopeDenied, CancellationToken token)
    {
        foreach (var partition in state.Partitions)
        {
            var request = TimeSeriesChunkQualificationRf3Data.Request(partition, state.Window) with
            {
                TagPointer = TimeSeriesChunkQualificationRf3Data.PrivatePath,
                TagValue = "alpha"
            };
            await ChunkAsync(reader, request, ErrorCode.PermissionDenied, state.Reader.Secret,
                scopeDenied ? TimeSeriesChunkQualificationRf3Data.ScopeDenied : TimeSeriesChunkQualificationRf3Data.FieldUseDenied,
                scopeDenied ? TimeSeriesChunkQualificationRf3Data.ScopeDenied : TimeSeriesChunkQualificationRf3Data.SafePermissionDenied, token);
        }
    }
    internal static async Task ChunkAsync(RequestCqrsRf3Callers callers,
        ReadSampleChunkWindowRequest request, ErrorCode code, string credential, string detail, string safe, CancellationToken token)
    {
        var sdk = await callers.Sdk.ReadSampleChunkWindowAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), sdk.Problem);
        await ProblemAsync(await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token), credential, code, safe);
        var call = SqlRf3Protocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        var sql = await callers.Sdk.ExecuteSqlAsync(call, token);
        await Assert.That(sql.IsFailed).IsTrue();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), sql.Problem);
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
    internal static Task BoundAsync(RequestCqrsRf3Callers reader, TimeSeriesChunkQualificationRf3State state, CancellationToken token)
    {
        var request = TimeSeriesChunkQualificationRf3Data.Request(state.Partitions[TimeSeriesChunkQualificationRf3Data.SiblingScope], state.Window) with
        {
            Limit = TimeSeriesChunkQualificationRf3Data.RefusedReadLimit,
            MinimumToken = state.Originals[TimeSeriesChunkQualificationRf3Data.SiblingScope].Receipt.Token
        };
        return ChunkAsync(reader, request, ErrorCode.BudgetExceeded, state.Reader.Secret,
            TimeSeriesChunkQualificationRf3Data.ReadBoundExceeded, TimeSeriesChunkQualificationRf3Data.SafeBoundExceeded, token);
    }
    internal static async Task CancellationAsync(RequestCqrsRf3Callers reader, TimeSeriesChunkQualificationRf3State state)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var originalToken = cancellation.Token;
        var request = TimeSeriesChunkQualificationRf3Data.Request(state.Partitions[TimeSeriesChunkQualificationRf3Data.SiblingScope], state.Window);
        var original = reader.Sdk.ReadSampleChunkWindowAsync(request, originalToken);
        var result = await original;
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, TimeSeriesChunkQualificationRf3Data.ReadUnavailable), result.Problem);
        var official = reader.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, originalToken);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await official);
        await Assert.That(failure!.CancellationToken).IsEqualTo(originalToken);
    }
}
