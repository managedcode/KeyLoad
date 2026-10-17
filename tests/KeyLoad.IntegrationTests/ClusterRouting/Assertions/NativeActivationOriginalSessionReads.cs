using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationOriginalSessionReads
{
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";

    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers,
        NativeActivationOriginalSessionState state, long revision, string json, int route, CancellationToken token)
    {
        var request = new GetDocumentRequest(state.Reference, state.Minimum);
        var expected = new DocumentReadResult(new(state.Reference, revision, json, false, []));
        var call = SqlRf3Protocol.Call(state.Reference.Partition, McpCallerTools.DocumentsGet, request);
        var actual = route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(state.Reference, state.Minimum, token)),
            1 => (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, request, token))).Value,
            2 => await SqlRf3Protocol.SdkAsync<DocumentReadResult>(callers.Sdk, call, token),
            3 => await SqlRf3Protocol.McpAsync<DocumentReadResult>(callers.Mcp, call, token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers,
        NativeActivationOriginalSessionState state, int route, CancellationToken token)
    {
        var request = new GetDocumentRequest(state.Reference, state.Minimum);
        var call = SqlRf3Protocol.Call(state.Reference.Partition, McpCallerTools.DocumentsGet, request);
        var expected = Errors.Problem(ErrorCode.PermissionDenied, DeniedDetail);
        if (route == 0)
        {
            var actual = await callers.Sdk.GetAsync(state.Reference, state.Minimum, token);
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value is null).IsTrue();
            await SqlRf3Protocol.EqualAsync(expected, actual.Problem);
        }
        else if (route == 2)
        {
            var actual = await callers.Sdk.ExecuteSqlAsync(call, token);
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await SqlRf3Protocol.EqualAsync(expected, actual.Problem);
        }
        else if (route is 1 or 3)
        {
            await DeniedMcpAsync(callers, request, call, expected, route, token);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }
    }
    private static async Task DeniedMcpAsync(RequestCqrsRf3Callers callers, GetDocumentRequest request,
        SqlOperationRequest call, ManagedCode.Communication.Problem expected, int route, CancellationToken token)
    {
        var actual = route == 1
            ? await callers.Mcp.CallAsync(McpCallerTools.DocumentsGet, request, token)
            : await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, call, token);
        await McpCallerAssertions.ErrorAsync(actual, ErrorCode.PermissionDenied, dispatched: true);
        var problem = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(expected));
        await SqlRf3Protocol.EqualAsync(problem, actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
    }
}
