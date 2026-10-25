using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateColdDropRefusals
{
    internal static async Task OriginalWriterAsync(FunctionalGateContext context, FunctionalGateColdModel model,
        FunctionalGateColdDropTable table, FunctionalGateColdDropState state, CancellationToken token)
    {
        var sdk = await context.Writer.Sdk.CommitAsync(table.OriginalInsert, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await ProblemAsync(sdk.Problem?.ErrorCode, sdk.Problem?.StatusCode);
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        _ = (await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            table.OriginalInsert, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        await SqlAsync(await context.Writer.Sdk.ExecuteSqlAsync(model.TableInsert, token));
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        _ = (await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            model.TableInsert, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
    }

    private static async Task SqlAsync(Result<JsonElement> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await ProblemAsync(result.Problem?.ErrorCode, result.Problem?.StatusCode);
    }

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
    }
}
