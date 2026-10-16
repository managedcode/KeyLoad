using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleReferences
{
    internal static async Task<ImmutableArray<EntityRef>> CaptureAsync(RequestCqrsRf3Callers administrator,
        DropTableLifecycleTable table, CancellationToken token)
    {
        var captured = ImmutableArray.CreateBuilder<EntityRef>();
        foreach (var expected in table.Rows)
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.GetAsync(expected.Reference, token));
            await Assert.That(actual.Document).IsNotNull();
            var reference = actual.Document!.Reference;
            await Assert.That(reference.Lifetime).IsNotNull();
            await Assert.That(reference.Lifetime!.Incarnation).IsEqualTo(table.Lifetime.Incarnation.Value);
            await SqlRf3Protocol.EqualAsync(actual, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
            captured.Add(reference);
        }
        return captured.ToImmutable();
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers administrator,
        ImmutableArray<EntityRef> originalReferences, Func<Task> unchanged, CancellationToken token)
    {
        foreach (var reference in originalReferences)
        {
            await Assert.That(reference.Lifetime).IsNotNull();
            var request = new GetDocumentRequest(reference);
            await TypedAsync(await administrator.Sdk.GetAsync(reference, token));
            await unchanged();
            await McpCallerAssertions.ErrorAsync(await administrator.Mcp.CallAsync(McpCallerTools.DocumentsGet,
                request, token), ErrorCode.OwnershipLost, dispatched: true);
            await unchanged();
            var sql = SqlRf3Protocol.Call(reference.Partition, McpCallerTools.DocumentsGet, request);
            var failed = await administrator.Sdk.ExecuteSqlAsync(sql, token);
            await Assert.That(failed.IsFailed).IsTrue();
            await Assert.That(failed.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await ProblemAsync(failed.Problem?.ErrorCode, failed.Problem?.StatusCode);
            await unchanged();
            await McpCallerAssertions.ErrorAsync(await administrator.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                sql, token), ErrorCode.OwnershipLost, dispatched: true);
            await unchanged();
        }
    }

    private static async Task TypedAsync(ManagedCode.Communication.Result<DocumentReadResult> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await ProblemAsync(result.Problem?.ErrorCode, result.Problem?.StatusCode);
    }

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.OwnershipLost));
    }
}
