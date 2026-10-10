using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class CompositionSqlColdAssertions
{
    internal static async Task OriginalAsync(RequestCqrsRf3Callers callers, CompositionSqlColdState state, CancellationToken token)
    {
        var actual = await CompositionSqlColdCapture.CaptureAsync(callers.Sdk, state.Scenario, state.Link,
            state.Forward, state.ForwardReceipt, state.Reverse, state.ReverseReceipt, token);
        await SqlRf3Protocol.EqualAsync(state.First, actual.First);
        await SqlRf3Protocol.EqualAsync(state.Second, actual.Second);
        await SqlRf3Protocol.EqualAsync(state.Graph, actual.Graph);
        await SqlRf3Protocol.EqualAsync(state.Source, actual.Source);
        await SqlRf3Protocol.EqualAsync(state.Derived, actual.Derived);
        await Assert.That(actual.Source.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Source.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(actual.Source.Metadata.LeaseOwner).IsNull();
        await Assert.That(actual.Derived.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Derived.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(actual.Derived.Metadata.LeaseOwner).IsNull();
        await Assert.That(actual.Source.PayloadJson).IsEqualTo(DatabaseCompositionRf3Payload.OrdinalGolden(state.Link));
        var traversal = new TraverseRequest(state.Scenario.Partition, RelationalSqlRf3Tokens.Graph, state.Scenario.First);
        await SqlRf3Protocol.EqualAsync(state.Graph, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await callers.Mcp.CallAsync(McpCallerTools.GraphTraverse, traversal, token))).Value);
        foreach (var (command, receipt) in new[] { (state.Forward, state.ForwardReceipt), (state.Reverse, state.ReverseReceipt) })
        { await ReplayAsync(callers, command, receipt, token); }
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token)));
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
    }

    internal static async Task OtherAsync(RequestCqrsRf3Callers callers, CompositionSqlColdState state,
        bool healthy, CancellationToken token)
    {
        var graph = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.TraverseAsync(new(state.Scenario.Partition,
            CompositionSqlColdProtocol.OtherGraph, state.Scenario.First), token));
        var marker = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(new(state.Scenario.Partition,
            RelationalSqlRf3Tokens.Documents, CompositionSqlColdProtocol.Marker), token));
        if (!healthy)
        {
            await Assert.That(graph.Edges).IsEmpty();
            await Assert.That(marker).IsNull();
            return;
        }
        await Assert.That(marker).IsNotNull();
        await Assert.That(marker!.Json).IsEqualTo(RelationalSqlRf3Tokens.EmptyJson);
        await Assert.That(graph.Edges.Length).IsEqualTo(CompositionSqlColdProtocol.HealthyEdges);
        var expectedIds = new[] { CompositionSqlColdProtocol.HealthyPrefix + CompositionSqlColdProtocol.Message,
            CompositionSqlColdProtocol.HealthyPrefix + CompositionSqlColdProtocol.MessagePrefix + CompositionSqlColdProtocol.EdgePrefix + CompositionSqlColdProtocol.Message };
        foreach (var edge in graph.Edges)
        {
            await Assert.That(expectedIds.Contains(edge.Id, StringComparer.Ordinal)).IsTrue();
            await Assert.That(edge.From).IsEqualTo(state.Scenario.First);
            await Assert.That(edge.To).IsEqualTo(state.Scenario.Second);
            await Assert.That(edge.Label).IsEqualTo(RelationalSqlRf3Tokens.EdgeLabel);
        }
    }
}
