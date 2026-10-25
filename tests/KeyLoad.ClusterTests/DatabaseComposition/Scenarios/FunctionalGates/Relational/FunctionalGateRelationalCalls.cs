using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

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
