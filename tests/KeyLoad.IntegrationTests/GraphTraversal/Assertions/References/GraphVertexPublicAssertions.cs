using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Messaging;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphVertexPublicAssertions
{
    private const int Version = 1;
    private const int Depth = 1;
    private const int MaximumVertices = 4;
    private const int MaximumEdges = 3;

    internal static async Task Models(RequestCqrsClusterCallers callers, GraphVertexPublicState state, CancellationToken token)
    {
        foreach (var start in new[] { state.Seed.LocalSource, state.NewVertex, state.Seed.Target })
        {
            var expectedEdges = state.Edges.Where(edge => edge.From == start).OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
            var expectedVertices = expectedEdges.Select(edge => edge.To).Append(start).Distinct()
                .OrderBy(vertex => vertex.Collection, StringComparer.Ordinal).ThenBy(vertex => vertex.Id, StringComparer.Ordinal).ToArray();
            var request = new TraverseRequest(start.Partition, state.Seed.Graph, start, Depth, MaximumVertices, MaximumEdges);
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.TraverseAsync(request, token));
            var mcp = (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
                await callers.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value;
            await Assert.That(sdk.Edges.SequenceEqual(expectedEdges)).IsTrue();
            await Assert.That(sdk.Vertices.SequenceEqual(expectedVertices)).IsTrue();
            await SqlClusterProtocol.EqualAsync(sdk, mcp);
            var incomingRequest = new ReadIncomingGraphEdgesRequest(Version, start, state.Seed.Graph, MaximumEdges);
            var incoming = await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.GraphIncomingEdgesClient.IncomingEdgesAsync(callers.Sdk, incomingRequest, token));
            var expectedIncoming = state.Edges.Where(edge => edge.To == start).OrderBy(edge => edge.From.Collection, StringComparer.Ordinal)
                .ThenBy(edge => edge.From.Id, StringComparer.Ordinal).ThenBy(edge => edge.Id, StringComparer.Ordinal)
                .Select(edge => new GraphIncomingEdgeRow(edge, 0L)).ToArray();
            await Assert.That(incoming.Rows.SequenceEqual(expectedIncoming)).IsTrue();
            var mcpIncoming = (await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(
                await callers.Mcp.CallAsync(McpCallerTools.GraphIncomingEdges, incomingRequest, token))).Value;
            await SqlClusterProtocol.EqualAsync(incoming.Rows, mcpIncoming.Rows);
            await Assert.That(incoming.Version).IsEqualTo(Version);
            await Assert.That(incoming.CutPosition).IsGreaterThan(0L);
        }
    }

    internal static async Task Originals(RequestCqrsClusterCallers callers, GraphVertexPublicState state, CancellationToken token)
    {
        foreach (var original in state.Original)
        {
            await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.CommitAsync(original.Command, token)));
            await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
        }
        foreach (var refused in state.Refusals)
        {
            var sdk = await callers.Sdk.CommitAsync(refused.Command, token);
            await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(sdk, refused.Code);
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, refused.Command, token), refused.Code, dispatched: true);
        }
        await Assert.That(await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(
            new(state.Seed.TargetPartition, GraphIncomingClusterScenario.Collection, GraphVertexPublicState.WitnessId), token))).IsNull();
    }
}
