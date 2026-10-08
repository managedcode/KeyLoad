using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Call
{
    private const string Arguments = "arguments";
    private const string Sql = "CALL keyload_search_text_maintain(@arguments)";

    internal static async Task<TextIndexMaintenanceResult> ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TextIndexMaintenanceRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        if (path == NativeTextMaintenancePath.Sdk)
        { return await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainTextIndexAsync(request, token)); }
        if (path == NativeTextMaintenancePath.Mcp)
        {
            return (await McpCallerAssertions.SuccessAsync<TextIndexMaintenanceResult>(
                await mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName, request, token))).Value;
        }
        var sql = new SqlOperationRequest(request.Consumer.Partition, Sql,
            new(StringComparer.Ordinal)
            {
                [Arguments] = JsonSerializer.SerializeToElement(
                McpOfficialClient.Arguments(request), JsonDefaults.Options)
            });
        if (path == NativeTextMaintenancePath.SdkSql)
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(sql, token));
            return actual.Deserialize<TextIndexMaintenanceResult>(JsonDefaults.Options)
                ?? throw new InvalidOperationException();
        }
        if (path != NativeTextMaintenancePath.McpSql)
        { throw new ArgumentOutOfRangeException(nameof(path)); }
        return (await McpCallerAssertions.SuccessAsync<TextIndexMaintenanceResult>(
            await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token))).Value;
    }
}
