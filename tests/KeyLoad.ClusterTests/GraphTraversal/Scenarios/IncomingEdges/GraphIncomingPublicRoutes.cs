using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal static class GraphIncomingPublicRoutes
{
    internal static async Task HealthyAsync(RequestCqrsClusterCallers callers, GraphIncomingClusterSeed seed,
        bool sourceHidden, CancellationToken token)
    {
        var request = GraphIncomingClusterScenario.Request(seed);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.IncomingEdgesAsync(request, token));
        await LiteralAsync(direct, seed, sourceHidden);
        var reply = await callers.Mcp.CallAsync(GraphIncomingClusterScenario.Tool, request, token);
        await LiteralAsync((await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(reply)).Value, seed, sourceHidden);
        await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(reply, seed.Reader.Secret);
        var sql = SqlClusterProtocol.Call(seed.TargetPartition, GraphIncomingClusterScenario.Tool, request);
        await LiteralAsync(await SqlClusterProtocol.SdkAsync<GraphIncomingEdgesPage>(callers.Sdk, sql, token), seed, sourceHidden);
        reply = await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await LiteralAsync((await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(reply)).Value, seed, sourceHidden);
        await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(reply, seed.Reader.Secret);
    }

    internal static async Task DeniedAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers, GraphIncomingClusterSeed seed,
        ErrorCode code, CancellationToken token)
    {
        var request = GraphIncomingClusterScenario.Request(seed);
        await SdkDeniedAsync(await callers.Sdk.IncomingEdgesAsync(request, token), code, seed.Reader.Secret);
        await McpDeniedAsync(callers, GraphIncomingClusterScenario.Tool, request, code, seed.Reader.Secret, token);
        var sql = SqlClusterProtocol.Call(seed.TargetPartition, GraphIncomingClusterScenario.Tool, request);
        await SdkDeniedAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), code, seed.Reader.Secret);
        await McpDeniedAsync(callers, SqlOperationProtocol.ToolName, sql, code, seed.Reader.Secret, token);
        if (code == ErrorCode.Unauthenticated)
        {
            await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, seed.Reader.Secret, token);
        }
    }

    private static async Task SdkDeniedAsync<T>(Result<T> result, ErrorCode code, string credential)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        if (result.Value is JsonElement element)
        {
            await Assert.That(element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null).IsTrue();
        }
        else
        {
            await McpCallerAssertions.NoValueAsync(result);
        }
        var text = Encoding.UTF8.GetString(JsonDefaults.Serialize(result.Problem));
        await Assert.That(text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(GraphIncomingClusterScenario.PrivateMarker, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task McpDeniedAsync<T>(RequestCqrsClusterCallers callers, string tool, T request,
        ErrorCode code, string credential, CancellationToken token)
    {
        if (code == ErrorCode.Unauthenticated)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => callers.Mcp.CallAsync(tool, request, token));
            await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(failure.Message.Contains(credential, StringComparison.Ordinal)).IsFalse();
            await Assert.That(failure.Message.Contains(GraphIncomingClusterScenario.PrivateMarker, StringComparison.Ordinal)).IsFalse();
            return;
        }
        var reply = await callers.Mcp.CallAsync(tool, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(reply, credential);
    }

    private static async Task LiteralAsync(GraphIncomingEdgesPage page, GraphIncomingClusterSeed seed, bool hidden)
    {
        if (hidden)
        {
            await GraphIncomingClusterAssertions.AssertSourceHiddenAsync(page, seed);
        }
        else
        {
            await GraphIncomingClusterAssertions.AssertThreeRowsAsync(page, seed);
        }
        await Assert.That(page.Version).IsEqualTo(GraphIncomingPublicPolicy.Version);
        await Assert.That(page.CutPosition).IsGreaterThan(GraphIncomingPublicPolicy.EmptyPosition);
    }
}
