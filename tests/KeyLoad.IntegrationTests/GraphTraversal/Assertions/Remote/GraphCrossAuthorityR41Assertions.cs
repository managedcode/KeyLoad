using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphCrossAuthorityR41Assertions
{
    internal static async Task CurrentAsync(RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        foreach (var reference in new[] { state.Seed.Target, state.Seed.LocalSource, state.Seed.FirstSource,
            state.Seed.SecondSource, state.Collision })
        {
            var expected = new DocumentResult(reference, 1L, "{}", false, []);
            await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                await administrator.Sdk.GetAsync(reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        await TraversalAsync(administrator, state.Request(), state.Expected(false), state.Seed.Reader.Secret, null, token);
        var collision = new TraverseRequest(state.Seed.TargetPartition, state.Seed.Graph, state.Collision, 3, 2, 1);
        var expectedCollision = new global::KeyLoad.GraphTraversal([state.Collision, state.Seed.Target], [state.Colliding]);
        await TraversalAsync(administrator, collision, expectedCollision, state.Seed.Reader.Secret, null, token);
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(administrator, state, token));
    }

    internal static async Task<OutboxStatus[]> OutboxAsync(RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        var values = new List<OutboxStatus>();
        foreach (var partition in new[] { state.Seed.TargetPartition, state.Seed.FirstSourcePartition, state.Seed.SecondSourcePartition })
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(partition, token));
            await SqlClusterProtocol.EqualAsync(sdk, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
                await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
            values.Add(sdk);
        }
        return [.. values];
    }

    internal static async Task MatrixAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        await TraversalAsync(reader, state.Request(state.Hidden ? 2 : 3), state.Expected(state.Hidden), state.Seed.Reader.Secret,
            () => CurrentAsync(administrator, state, token), token);
        await TraversalAsync(reader, state.Request(2, filtered: true), state.Expected(state.Hidden, true), state.Seed.Reader.Secret,
            () => CurrentAsync(administrator, state, token), token);
    }

    private static async Task TraversalAsync(RequestCqrsClusterCallers callers, TraverseRequest request,
        global::KeyLoad.GraphTraversal expected, string credential, Func<Task>? observe,
        CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.TraverseAsync(request, token)));
        if (observe is not null)
        {
            await observe();
        }
        var reply = await callers.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token);
        await SqlClusterProtocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(reply)).Value);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential,
            GraphIncomingClusterScenario.PrivateMarker);
        if (observe is not null)
        {
            await observe();
        }
    }
}
