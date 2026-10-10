using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingPublicRoutes
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers, GraphIncomingRf3Seed seed,
        bool sourceHidden, CancellationToken token)
    {
        var request = GraphIncomingRf3Scenario.Request(seed);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.IncomingEdgesAsync(request, token));
        await LiteralAsync(direct, seed, sourceHidden);
        var reply = await callers.Mcp.CallAsync(GraphIncomingRf3Scenario.Tool, request, token);
        await LiteralAsync((await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPageV1>(reply)).Value, seed, sourceHidden);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(reply, seed.Reader.Secret);
        var sql = SqlRf3Protocol.Call(seed.TargetPartition, GraphIncomingRf3Scenario.Tool, request);
        await LiteralAsync(await SqlRf3Protocol.SdkAsync<GraphIncomingEdgesPageV1>(callers.Sdk, sql, token), seed, sourceHidden);
        reply = await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await LiteralAsync((await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPageV1>(reply)).Value, seed, sourceHidden);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(reply, seed.Reader.Secret);
    }

    internal static async Task DeniedAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers, GraphIncomingRf3Seed seed,
        ErrorCode code, CancellationToken token)
    {
        var request = GraphIncomingRf3Scenario.Request(seed);
        await SdkDeniedAsync(await callers.Sdk.IncomingEdgesAsync(request, token), code, seed.Reader.Secret);
        await McpDeniedAsync(callers, GraphIncomingRf3Scenario.Tool, request, code, seed.Reader.Secret, token);
        var sql = SqlRf3Protocol.Call(seed.TargetPartition, GraphIncomingRf3Scenario.Tool, request);
        await SdkDeniedAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), code, seed.Reader.Secret);
        await McpDeniedAsync(callers, SqlOperationProtocol.ToolName, sql, code, seed.Reader.Secret, token);
        if (code == ErrorCode.Unauthenticated)
        { await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, seed.Reader.Secret, token); }
    }

    private static async Task SdkDeniedAsync<T>(Result<T> result, ErrorCode code, string credential)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        if (result.Value is JsonElement element)
        { await Assert.That(element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null).IsTrue(); }
        else
        { await Assert.That(result.Value).IsNull(); }
        var text = Encoding.UTF8.GetString(JsonDefaults.Serialize(result.Problem));
        await Assert.That(text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(GraphIncomingRf3Scenario.PrivateMarker, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task McpDeniedAsync<T>(RequestCqrsRf3Callers callers, string tool, T request,
        ErrorCode code, string credential, CancellationToken token)
    {
        if (code == ErrorCode.Unauthenticated)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => callers.Mcp.CallAsync(tool, request, token));
            await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(failure.Message.Contains(credential, StringComparison.Ordinal)).IsFalse();
            await Assert.That(failure.Message.Contains(GraphIncomingRf3Scenario.PrivateMarker, StringComparison.Ordinal)).IsFalse();
            return;
        }
        var reply = await callers.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(reply, credential);
    }

    private static async Task LiteralAsync(GraphIncomingEdgesPageV1 page, GraphIncomingRf3Seed seed, bool hidden)
    {
        if (hidden)
        { await GraphIncomingRf3Assertions.AssertSourceHiddenAsync(page, seed); }
        else
        { await GraphIncomingRf3Assertions.AssertThreeRowsAsync(page, seed); }
        await Assert.That(page.Version).IsEqualTo(GraphIncomingPublicPolicy.Version);
        await Assert.That(page.CutPosition).IsGreaterThan(GraphIncomingPublicPolicy.EmptyPosition);
    }
}
