using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleDenials
{
    private const string IdentifierParameter = "id";

    internal static async Task DeniedAsync(RequestCqrsRf3Callers denied, PartitionRef partition,
        string table, EntityRef row, CommandRequest attemptedWrite, Func<Task> unchanged, CancellationToken token)
    {
        var drop = new DropTableRequest(partition.TenantId, partition.DatabaseId, table);
        var command = Guid.NewGuid();
        await DropTableLifecycleDeniedResult.TypedAsync(await denied.Sdk.DropTableAsync(command, drop, token));
        await unchanged();
        var arguments = DropLifecycleRf3Calls.Arguments(command, drop);
        await DropTableLifecycleDeniedResult.McpAsync(await denied.Mcp.Client.InvokeKeyLoadToolAsync(DropLifecycleRf3Calls.Tool, arguments, token));
        await unchanged();
        var sql = DropLifecycleRf3Calls.Sql(command, drop, partition);
        await DropTableLifecycleDeniedResult.SqlAsync(await denied.Sdk.ExecuteSqlAsync(sql, token));
        await unchanged();
        await DropTableLifecycleDeniedResult.McpAsync(await denied.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token));
        await unchanged();
        await ReadAsync(denied, partition, table, row, unchanged, token);
        await WriteAsync(denied, attemptedWrite, unchanged, token);
    }

    private static async Task ReadAsync(RequestCqrsRf3Callers denied, PartitionRef partition,
        string table, EntityRef row, Func<Task> unchanged, CancellationToken token)
    {
        await DropTableLifecycleDeniedResult.TypedAsync(await denied.Sdk.GetAsync(row, token));
        await unchanged();
        await DropTableLifecycleDeniedResult.McpAsync(await denied.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(row), token));
        await unchanged();
        var sql = new SqlOperationRequest(partition, "SELECT id, title, amount FROM [" + table + "] WHERE id = @id",
            new(StringComparer.Ordinal)
            {
                [IdentifierParameter] = JsonSerializer.SerializeToElement(row.Id)
            });
        await DropTableLifecycleDeniedResult.SqlAsync(await denied.Sdk.ExecuteSqlAsync(sql, token));
        await unchanged();
        await DropTableLifecycleDeniedResult.McpAsync(await denied.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token));
        await unchanged();
    }

    private static async Task WriteAsync(RequestCqrsRf3Callers denied, CommandRequest command,
        Func<Task> unchanged, CancellationToken token)
    {
        await DropTableLifecycleDeniedResult.TypedAsync(await denied.Sdk.CommitAsync(command, token));
        await unchanged();
        await DropTableLifecycleDeniedResult.McpAsync(await denied.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await unchanged();
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command) with
        {
            CommandId = command.CommandId
        };
        await DropTableLifecycleDeniedResult.SqlAsync(await denied.Sdk.ExecuteSqlAsync(sql, token));
        await unchanged();
        await DropTableLifecycleDeniedResult.McpAsync(await denied.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token));
        await unchanged();
    }
}
