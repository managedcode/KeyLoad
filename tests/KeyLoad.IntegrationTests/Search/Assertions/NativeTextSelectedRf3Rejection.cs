using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedRf3Rejection
{
    private const string Mismatch = "The native text projection does not match the authorized source cut.";

    internal static async Task AssertAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SearchRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        if (path == NativeTextMaintenancePath.Sdk)
        {
            var actual = await sdk.SearchAsync(request, token);
            await ErrorAsync(actual);
            await Assert.That(actual.Value).IsNull();
            return;
        }
        if (path == NativeTextMaintenancePath.SdkSql)
        {
            var actual = await sdk.ExecuteSqlAsync(NativeTextSelectedRf3Call.SqlRequest(request), token);
            await ErrorAsync(actual);
            await Assert.That(actual.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
            return;
        }
        var reply = path switch
        {
            NativeTextMaintenancePath.Mcp => await mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
            NativeTextMaintenancePath.McpSql => await mcp.CallAsync(SqlOperationProtocol.ToolName,
                NativeTextSelectedRf3Call.SqlRequest(request), token),
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };
        _ = await McpCallerAssertions.ErrorAsync(reply, ErrorCode.HistoryUnavailable, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Mismatch);
    }

    private static async Task ErrorAsync<T>(Result<T> actual)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(ErrorCode.HistoryUnavailable.ToString());
        await Assert.That(actual.Problem?.Detail).IsEqualTo(Mismatch);
    }
}
