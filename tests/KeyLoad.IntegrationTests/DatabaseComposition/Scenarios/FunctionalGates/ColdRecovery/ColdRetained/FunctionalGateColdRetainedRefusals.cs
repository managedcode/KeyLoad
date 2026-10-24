using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateColdRetainedRefusals
{
    internal static async Task AllAsync(FunctionalGateContext context, CommandRequest command,
        Func<FunctionalGateContext, Task> unchanged, CancellationToken token)
    {
        var sdk = await context.Writer.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(sdk.Problem?.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
        await unchanged(context);
        await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            command, token), ErrorCode.PermissionDenied, dispatched: true);
        await unchanged(context);
        var sql = FunctionalGateRelationalCalls.Commit(command);
        var sqlSdk = await context.Writer.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlSdk.IsFailed).IsTrue();
        await Assert.That(sqlSdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(sqlSdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(sqlSdk.Problem?.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
        await unchanged(context);
        await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            sql, token), ErrorCode.PermissionDenied, dispatched: true);
        await unchanged(context);
    }
}
