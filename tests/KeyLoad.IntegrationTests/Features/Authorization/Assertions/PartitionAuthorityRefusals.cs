using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PartitionAuthorityRefusals
{
    internal static async Task ReadAsync(KeyLoadClient sdk, McpOfficialClient session, PartitionAuthorityState state,
        ErrorCode error, CancellationToken token)
    {
        var request = state.Scenario.Request();
        var direct = await sdk.PartitionQueryAsync(request, token);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(error.ToString());
        await SafeAsync(JsonSerializer.Serialize(direct, JsonDefaults.Options), state.Identity.Secret);
        var call = SqlRf3Protocol.Call(state.Scenario.Partitions[PartitionAuthorityProtocol.FirstLeaf],
            PartitionQueryRf3Protocol.ToolName, request);
        var q1 = await sdk.ExecuteSqlAsync(call, token);
        await Assert.That(q1.IsSuccess).IsFalse();
        await Assert.That(q1.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(q1.Problem?.ErrorCode).IsEqualTo(error.ToString());
        await SafeAsync(JsonSerializer.Serialize(q1.Problem, JsonDefaults.Options), state.Identity.Secret);
        await OfficialAsync(async () => await session.CallAsync(PartitionQueryRf3Protocol.ToolName,
            request, token), state.Identity.Secret, error);
        await OfficialAsync(async () => await session.CallAsync(SqlOperationProtocol.ToolName,
            call, token), state.Identity.Secret, error);
    }

    private static async Task OfficialAsync(Func<Task<ModelContextProtocol.Protocol.CallToolResult>> operation, string key, ErrorCode error)
    {
        if (error == ErrorCode.Unauthenticated)
        {
            var refused = false;
            try
            { await operation(); }
            catch (HttpRequestException original) when (original.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { refused = true; await SafeAsync(original.Message, key); }
            await Assert.That(refused).IsTrue();
            return;
        }
        var reply = await operation();
        await McpCallerAssertions.ErrorAsync(reply, error, dispatched: true);
        await SafeAsync(JsonSerializer.Serialize(reply), key);
    }

    private static async Task SafeAsync(string text, string key)
    {
        foreach (var canary in new[] { key, PartitionAuthorityProtocol.FirstId, PartitionAuthorityProtocol.SecondId,
            PartitionAuthorityProtocol.FirstProjection, PartitionAuthorityProtocol.SecondProjection })
        { await Assert.That(text.Contains(canary, StringComparison.Ordinal)).IsFalse(); }
    }
}
