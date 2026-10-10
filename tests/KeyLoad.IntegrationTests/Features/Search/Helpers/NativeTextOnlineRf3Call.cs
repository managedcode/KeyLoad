using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextOnlineRf3Call
{
    private const string Arguments = "arguments";
    private const string Sql = "CALL keyload_search_text_online_maintain(@arguments)";
    internal static SqlOperationRequest SqlRequest(OnlineTextIndexMaintenanceRequest request)
        => new(request.Consumer.Partition, Sql, new(StringComparer.Ordinal)
        { [Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(request), JsonDefaults.Options) });

    internal static async Task<OnlineTextIndexMaintenanceResult> ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        OnlineTextIndexMaintenanceRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        if (path == NativeTextMaintenancePath.Sdk)
        { return await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainOnlineTextIndexAsync(request, token)); }
        if (path == NativeTextMaintenancePath.Mcp)
        { return (await McpCallerAssertions.SuccessAsync<OnlineTextIndexMaintenanceResult>(await mcp.CallAsync(OnlineTextIndexMaintenanceProtocol.Tool, request, token))).Value; }
        if (path == NativeTextMaintenancePath.SdkSql)
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(SqlRequest(request), token));
            return actual.Deserialize<OnlineTextIndexMaintenanceResult>(JsonDefaults.Options) ?? throw new InvalidOperationException();
        }
        if (path != NativeTextMaintenancePath.McpSql)
        { throw new ArgumentOutOfRangeException(nameof(path)); }
        return (await McpCallerAssertions.SuccessAsync<OnlineTextIndexMaintenanceResult>(await mcp.CallAsync(SqlOperationProtocol.ToolName, SqlRequest(request), token))).Value;
    }

    internal static async Task RejectedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        OnlineTextIndexMaintenanceRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        if (path == NativeTextMaintenancePath.Sdk)
        {
            var actual = await sdk.MaintainOnlineTextIndexAsync(request, token);
            await RequireErrorAsync(actual);
            await Assert.That(actual.Value).IsNull();
            return;
        }
        if (path == NativeTextMaintenancePath.SdkSql)
        {
            var actual = await sdk.ExecuteSqlAsync(SqlRequest(request), token);
            await RequireErrorAsync(actual);
            await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            return;
        }
        var reply = path switch
        {
            NativeTextMaintenancePath.Mcp => await mcp.CallAsync(OnlineTextIndexMaintenanceProtocol.Tool, request, token),
            NativeTextMaintenancePath.McpSql => await mcp.CallAsync(SqlOperationProtocol.ToolName, SqlRequest(request), token),
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };
        _ = await McpCallerAssertions.ErrorAsync(reply, ErrorCode.OwnershipLost, dispatched: true);
    }

    private static async Task RequireErrorAsync<T>(Result<T> actual)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
    }
}
