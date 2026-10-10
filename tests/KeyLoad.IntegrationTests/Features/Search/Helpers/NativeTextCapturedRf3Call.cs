using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextCapturedRf3Observation(RankedDocument[]? Rows, string? SdkError,
    CallToolResult? McpError, Exception? TransportFailure);

internal static class NativeTextCapturedRf3Call
{
    internal static async Task<NativeTextCapturedRf3Observation> ExecuteAsync(RequestCqrsRf3Callers caller,
        SearchRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        try
        {
            if (path == NativeTextMaintenancePath.Sdk)
            {
                var result = await caller.Sdk.SearchAsync(request, token);
                return result.IsSuccess ? new(await McpCallerAssertions.SdkSuccessAsync(result), null, null, null)
                    : new(null, result.Problem?.ErrorCode, null, null);
            }
            if (path == NativeTextMaintenancePath.SdkSql)
            {
                var result = await caller.Sdk.ExecuteSqlAsync(NativeTextSelectedRf3Call.SqlRequest(request), token);
                if (!result.IsSuccess)
                { return new(null, result.Problem?.ErrorCode, null, null); }
                var json = await McpCallerAssertions.SdkSuccessAsync(result);
                return new(json.Deserialize<RankedDocument[]>(JsonDefaults.Options) ?? throw new InvalidOperationException(), null, null, null);
            }
            var reply = path switch
            {
                NativeTextMaintenancePath.Mcp => await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
                NativeTextMaintenancePath.McpSql => await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                    NativeTextSelectedRf3Call.SqlRequest(request), token),
                _ => throw new ArgumentOutOfRangeException(nameof(path))
            };
            return reply.IsError is true ? new(null, null, reply, null)
                : new((await McpCallerAssertions.SuccessAsync<RankedDocument[]>(reply)).Value, null, null, null);
        }
        catch (OperationCanceledException error) { return new(null, null, null, error); }
        catch (HttpRequestException error) { return new(null, null, null, error); }
        catch (IOException error) { return new(null, null, null, error); }
    }

    internal static async Task CancelledAsync(NativeTextCapturedRf3Observation actual, bool cancelled)
    {
        await Assert.That(cancelled).IsTrue();
        await Assert.That(actual.Rows).IsNull();
        if (actual.TransportFailure is not null)
        { await Assert.That(actual.TransportFailure is OperationCanceledException or HttpRequestException or IOException).IsTrue(); return; }
        if (actual.McpError is not null)
        { _ = await McpCallerAssertions.ErrorAsync(actual.McpError, ErrorCode.Cancelled, true); return; }
        await Assert.That(actual.SdkError).IsEqualTo(ErrorCode.Cancelled.ToString());
    }
}
