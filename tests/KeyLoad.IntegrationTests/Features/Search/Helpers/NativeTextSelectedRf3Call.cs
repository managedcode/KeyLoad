using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedRf3Call
{
    private const string Arguments = "arguments";
    private const string Sql = "CALL keyload_search_execute(@arguments)";
    internal static SqlOperationRequest SqlRequest(SearchRequest request)
        => new(request.Partition, Sql, new(StringComparer.Ordinal)
        { [Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(request), JsonDefaults.Options) });

    internal static async Task<RankedDocument[]> ExecuteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SearchRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        if (path == NativeTextMaintenancePath.Sdk)
        { return await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token)); }
        if (path == NativeTextMaintenancePath.Mcp)
        { return (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value; }
        var sql = SqlRequest(request);
        if (path == NativeTextMaintenancePath.SdkSql)
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(sql, token));
            return actual.Deserialize<RankedDocument[]>(JsonDefaults.Options) ?? throw new InvalidOperationException();
        }
        if (path != NativeTextMaintenancePath.McpSql)
        { throw new ArgumentOutOfRangeException(nameof(path)); }
        return (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token))).Value;
    }
}
