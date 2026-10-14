using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SqlLabelAuthorityRefusals
{
    internal static async Task DeniedAsync(KeyLoadClient sdk, McpOfficialClient session,
        GraphSearchRf3Scenario scenario, string key, CancellationToken token)
    {
        var request = SqlGraphSearchRf3Scenario.Request(scenario.Partition, SqlGraphSearchRf3Scenario.LabeledStatement);
        var denied = await sdk.SearchSqlAsync(request, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await SafeAsync(JsonSerializer.Serialize(denied, JsonDefaults.Options), key);
        var direct = await session.CallAsync(SqlGraphSearchRf3Scenario.QuerySearchTool, request, token);
        await McpCallerAssertions.ErrorAsync(direct, ErrorCode.PermissionDenied, dispatched: true);
        await SafeAsync(JsonSerializer.Serialize(direct), key);
        var call = SqlRf3Protocol.Call(scenario.Partition, SqlGraphSearchRf3Scenario.QuerySearchTool, request);
        var q1 = await sdk.ExecuteSqlAsync(call, token);
        await Assert.That(q1.IsSuccess).IsFalse();
        await Assert.That(q1.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(q1.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await SafeAsync(JsonSerializer.Serialize(q1.Problem, JsonDefaults.Options), key);
        var official = await session.CallAsync(SqlOperationProtocol.ToolName, call, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await SafeAsync(JsonSerializer.Serialize(official), key);
    }

    internal static async Task RevokedAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient session,
        SqlLabelAuthorityState state, CancellationToken token)
    {
        foreach (var statement in new[] { SqlGraphSearchRf3Scenario.LabeledStatement, SqlGraphSearchRf3Scenario.EmptyStatement })
        {
            var request = SqlGraphSearchRf3Scenario.Request(state.Scenario.Partition, statement);
            var denied = await sdk.SearchSqlAsync(request, token);
            await Assert.That(denied.IsSuccess).IsFalse();
            await Assert.That(denied.Value).IsNull();
            await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
            await SafeAsync(JsonSerializer.Serialize(denied, JsonDefaults.Options), state.Identity.Secret);
            await UnauthorizedAsync(async () =>
            {
                await session.CallAsync(
                SqlGraphSearchRf3Scenario.QuerySearchTool, request, token);
            }, state.Identity.Secret);
            var call = SqlRf3Protocol.Call(state.Scenario.Partition, SqlGraphSearchRf3Scenario.QuerySearchTool, request);
            var q1 = await sdk.ExecuteSqlAsync(call, token);
            await Assert.That(q1.IsSuccess).IsFalse();
            await Assert.That(q1.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await Assert.That(q1.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
            await SafeAsync(JsonSerializer.Serialize(q1.Problem, JsonDefaults.Options), state.Identity.Secret);
            await UnauthorizedAsync(async () =>
            {
                await session.CallAsync(
                SqlOperationProtocol.ToolName, call, token);
            }, state.Identity.Secret);
        }
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node1, state.Identity.Secret, token);
    }

    private static async Task UnauthorizedAsync(Func<Task> operation, string key)
    {
        var refused = false;
        try
        {
            await operation();
        }
        catch (HttpRequestException original) when (original.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            refused = true;
            await SafeAsync(original.Message, key);
        }
        await Assert.That(refused).IsTrue();
    }

    private static async Task SafeAsync(string actual, string key)
    {
        foreach (var canary in new[] { key, GraphSearchRf3Scenario.Label, GraphSearchRf3Scenario.Alpha,
            GraphSearchRf3Scenario.Beta, GraphSearchRf3Scenario.Gamma, SqlGraphSearchRf3Scenario.LabeledStatement })
        {
            await Assert.That(actual.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
    }
}
