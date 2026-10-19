using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateRelationalCalls
{
    internal static async Task<T> SqlSdkAsync<T>(FunctionalGateContext ctx, RequestCqrsClusterCallers caller,
        SqlOperationRequest request, CancellationToken token)
    {
        var actual = await ctx.SdkAsync(caller.Sdk.ExecuteSqlAsync(request, token));
        return actual.Deserialize<T>(JsonDefaults.Options)!;
    }

    internal static Task<T> SqlMcpAsync<T>(FunctionalGateContext ctx, RequestCqrsClusterCallers caller,
        SqlOperationRequest request, CancellationToken token)
        => ctx.McpAsync<T>(caller, SqlOperationProtocol.ToolName, request, token);

    internal static SqlOperationRequest Query(FunctionalGateContext ctx, string text, string id)
        => new(ctx.Partition, text, FunctionalGateRelationalRows.Parameters(id), AllowFullScan: true,
            QueryDialectVersion: P.Dialect);

    internal static SqlOperationRequest Commit(CommandRequest command)
        => SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command)
            with
        {
            CommandId = command.CommandId
        };
}
