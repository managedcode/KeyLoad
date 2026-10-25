using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class QueryOnlyEpochR46Refusals
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryOnlyEpochR46State state, bool repaired, CancellationToken token)
    {
        foreach (var page in state.GrantedPages)
        {
            await RequestAsync(root, reader, state,
                PolicyEpochPublicProtocol.Request(state.Original.Scenario.Partition, page.Cursor),
                repaired ? ErrorCode.CursorExpired : ErrorCode.PermissionDenied, token);
        }
        if (!repaired)
        {
            await RequestAsync(root, reader, state,
                PolicyEpochPublicProtocol.Request(state.Original.Scenario.Partition), ErrorCode.PermissionDenied, token);
        }
    }

    private static async Task RequestAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryOnlyEpochR46State state, QueryRequest request,
        ErrorCode code, CancellationToken token)
    {
        var result = await reader.Sdk.QueryAsync(request, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, code == ErrorCode.PermissionDenied
            ? QueryOnlyEpochR46Protocol.ScopeDetail : QueryOnlyEpochR46Protocol.CursorDetail), result.Problem);
        await PrivateAsync(JsonSerializer.Serialize(result), state.CurrentReader.Secret);
        await QueryOnlyEpochR46Current.RequireAsync(root, reader, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(QueryOnlyEpochR46Protocol.DetailKey).GetString()).IsEqualTo(code == ErrorCode.PermissionDenied
                ? QueryOnlyEpochR46Protocol.SafeScopeDetail : QueryOnlyEpochR46Protocol.SafeCursorDetail);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.CurrentReader.Secret, ResourcePolicyUpdateClusterProtocol.SecretValue);
        await QueryOnlyEpochR46Current.RequireAsync(root, reader, state, token);
    }

    private static async Task PrivateAsync(string serialized, string credential)
    {
        await Assert.That(serialized.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(serialized.Contains(ResourcePolicyUpdateClusterProtocol.SecretValue, StringComparison.Ordinal)).IsFalse();
    }
}
