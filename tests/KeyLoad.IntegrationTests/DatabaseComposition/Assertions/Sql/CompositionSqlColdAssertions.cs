using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class CompositionSqlColdAssertions
{
    internal static async Task OriginalAsync(RequestCqrsClusterCallers callers, CompositionSqlColdState state, CancellationToken token)
    {
        var actual = await CompositionSqlColdCapture.CaptureAsync(callers.Sdk, state.Scenario, state.Link,
            state.Forward, state.ForwardReceipt, state.Reverse, state.ReverseReceipt, token);
        await SqlClusterProtocol.EqualAsync(state.First, actual.First);
        await SqlClusterProtocol.EqualAsync(state.Second, actual.Second);
        await SqlClusterProtocol.EqualAsync(state.Graph, actual.Graph);
        await SqlClusterProtocol.EqualAsync(state.Source, actual.Source);
        await SqlClusterProtocol.EqualAsync(state.Derived, actual.Derived);
        await Assert.That(actual.Source.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Source.Metadata.Attempts).IsEqualTo(RelationalSqlClusterTokens.NoResults);
        await Assert.That(actual.Source.Metadata.LeaseOwner).IsNull();
        await Assert.That(actual.Derived.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Derived.Metadata.Attempts).IsEqualTo(RelationalSqlClusterTokens.NoResults);
        await Assert.That(actual.Derived.Metadata.LeaseOwner).IsNull();
        await Assert.That(actual.Source.PayloadJson).IsEqualTo(DatabaseCompositionClusterPayload.OrdinalGolden(state.Link));
        var traversal = new TraverseRequest(state.Scenario.Partition, RelationalSqlClusterTokens.Graph, state.Scenario.First);
        await SqlClusterProtocol.EqualAsync(state.Graph, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await callers.Mcp.CallAsync(McpCallerTools.GraphTraverse, traversal, token))).Value);
        foreach (var (command, receipt) in new[] { (state.Forward, state.ForwardReceipt), (state.Reverse, state.ReverseReceipt) })
        {
            await ReplayAsync(callers, command, receipt, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers callers, CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token)));
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command) with
        {
            CommandId = command.CommandId
        };
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
    }

    internal static async Task OtherAsync(RequestCqrsClusterCallers callers, CompositionSqlColdState state,
        bool healthy, CancellationToken token)
    {
        var graph = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.TraverseAsync(new(state.Scenario.Partition,
            CompositionSqlColdProtocol.OtherGraph, state.Scenario.First), token));
        var marker = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(new(state.Scenario.Partition,
            RelationalSqlClusterTokens.Documents, CompositionSqlColdProtocol.Marker), token));
        if (!healthy)
        {
            await Assert.That(graph.Edges).IsEmpty();
            await Assert.That(marker).IsNull();
            return;
        }
        await Assert.That(marker).IsNotNull();
        await Assert.That(marker!.Json).IsEqualTo(RelationalSqlClusterTokens.EmptyJson);
        await Assert.That(graph.Edges.Length).IsEqualTo(CompositionSqlColdProtocol.HealthyEdges);
        var expectedIds = new[] { CompositionSqlColdProtocol.HealthyPrefix + CompositionSqlColdProtocol.Message,
            CompositionSqlColdProtocol.HealthyPrefix + CompositionSqlColdProtocol.MessagePrefix + CompositionSqlColdProtocol.EdgePrefix + CompositionSqlColdProtocol.Message };
        foreach (var edge in graph.Edges)
        {
            await Assert.That(expectedIds.Contains(edge.Id, StringComparer.Ordinal)).IsTrue();
            await Assert.That(edge.From).IsEqualTo(state.Scenario.First);
            await Assert.That(edge.To).IsEqualTo(state.Scenario.Second);
            await Assert.That(edge.Label).IsEqualTo(RelationalSqlClusterTokens.EdgeLabel);
        }
    }
}
