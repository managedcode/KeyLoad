using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsIdentityConflict
{
    internal static async Task VerifyAsync(FunctionalGateContext context, CommandRequest original,
        Func<Task> unchanged, CancellationToken token)
    {
        var changed = FunctionalGateAnalyticsIdentity.Changed(original);
        await Assert.That(changed.CommandId).IsEqualTo(original.CommandId);
        var sdk = await context.Writer.Sdk.CommitAsync(changed, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await ProblemAsync(sdk.Problem?.ErrorCode, sdk.Problem?.StatusCode);
        await unchanged();
        await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            FunctionalGateAnalyticsIdentity.Wire(changed), token), ErrorCode.Conflict, dispatched: true);
        await unchanged();
        var sql = SqlRf3Protocol.Call(context.Partition, McpCallerTools.DocumentsCommit,
            FunctionalGateAnalyticsIdentity.Wire(changed)) with
        {
            CommandId = original.CommandId
        };
        var sqlSdk = await context.Writer.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlSdk.IsFailed).IsTrue();
        await Assert.That(sqlSdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await ProblemAsync(sqlSdk.Problem?.ErrorCode, sqlSdk.Problem?.StatusCode);
        await unchanged();
        await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            sql, token), ErrorCode.Conflict, dispatched: true);
        await unchanged();
    }

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.Conflict));
    }
}
