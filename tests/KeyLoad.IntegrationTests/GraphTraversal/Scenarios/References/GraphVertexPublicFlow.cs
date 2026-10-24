using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphVertexPublicFlow
{
    internal static async Task Run(ClusterFixture fixture, CancellationToken token)
    {
        var seed = await GraphIncomingClusterScenario.CreateAsync(fixture, token);
        var state = new GraphVertexPublicState(seed);
        await Owned(fixture, state, async callers =>
        {
            var initial = GraphVertexPublicCommands.Command(state,
                new PutDocument(GraphIncomingClusterScenario.Collection, GraphVertexPublicState.NewId, GraphVertexPublicState.Json),
                GraphVertexPublicCommands.Upsert(state, seed.Target, 0L));
            await GraphVertexPublicCommands.Commit(callers, state, initial,
                [GraphVertexPublicCommands.PutReceipt(1L), GraphVertexPublicCommands.EdgeReceipt(state, 1L)], token);
            state.Edges = [Edge(state, seed.Target, 1L)];
            await GraphVertexPublicAssertions.Models(callers, state, token);
            await GraphVertexPublicCommands.Refuse(callers, state, initial with
            {
                Mutations = [new PutDocument(GraphIncomingClusterScenario.Collection, GraphVertexPublicState.WitnessId, GraphVertexPublicState.Json)]
            }, ErrorCode.Conflict, token);
            var missing = GraphVertexPublicCommands.Command(state,
                new PutDocument(GraphIncomingClusterScenario.Collection, GraphVertexPublicState.WitnessId, GraphVertexPublicState.Json),
                GraphVertexPublicCommands.Upsert(state, seed.Target with
                {
                    Id = GraphVertexPublicState.MissingId
                }, 1L));
            await GraphVertexPublicCommands.Refuse(callers, state, missing, ErrorCode.NotFound, token);
            var stale = GraphVertexPublicCommands.Command(state,
                new PutDocument(GraphIncomingClusterScenario.Collection, GraphVertexPublicState.WitnessId, GraphVertexPublicState.Json),
                GraphVertexPublicCommands.Upsert(state, state.NewVertex, 0L));
            await GraphVertexPublicCommands.Refuse(callers, state, stale, ErrorCode.RevisionConflict, token);
            await GraphVertexPublicCommands.Commit(callers, state,
                GraphVertexPublicCommands.Command(state, GraphVertexPublicCommands.Upsert(state, state.NewVertex, 1L)),
                [GraphVertexPublicCommands.EdgeReceipt(state, 2L)], token);
            state.Edges = [Edge(state, state.NewVertex, 2L)];
            await GraphVertexPublicAssertions.Models(callers, state, token);
            await GraphVertexPublicCommands.Commit(callers, state, GraphVertexPublicCommands.Command(state,
                new DeleteEdge(seed.Graph, GraphVertexPublicState.EdgeId, 2L)),
                [GraphVertexPublicCommands.EdgeReceipt(state, 3L, true)], token);
            state.Edges = [];
            await GraphVertexPublicAssertions.Models(callers, state, token);
            await GraphVertexPublicCommands.Commit(callers, state,
                GraphVertexPublicCommands.Command(state, GraphVertexPublicCommands.Upsert(state, state.NewVertex, 3L)),
                [GraphVertexPublicCommands.EdgeReceipt(state, 4L)], token);
            state.Edges = [Edge(state, state.NewVertex, 4L)];
        }, token);
        await Cold(fixture, state, token);
        await Owned(fixture, state, async callers =>
        {
            var healthy = new UpsertEdge(seed.Graph, GraphVertexPublicState.HealthyId, state.NewVertex, seed.Target,
                GraphVertexPublicState.Label, GraphVertexPublicState.Json, 0L);
            await GraphVertexPublicCommands.Commit(callers, state, GraphVertexPublicCommands.Command(state, healthy),
                [GraphVertexPublicCommands.HealthyReceipt(state)], token);
            state.Edges = [Edge(state, state.NewVertex, 4L), new(GraphVertexPublicState.HealthyId, state.NewVertex,
                seed.Target, GraphVertexPublicState.Label, GraphVertexPublicState.Json, 1L)];
        }, token);
        await Cold(fixture, state, token);
    }

    private static EdgeRecord Edge(GraphVertexPublicState state, EntityRef target, long revision)
        => new(GraphVertexPublicState.EdgeId, state.Seed.LocalSource, target,
            GraphVertexPublicState.Label, GraphVertexPublicState.Json, revision);

    private static async Task Cold(ClusterFixture fixture, GraphVertexPublicState state, CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await Owned(fixture, state, _ => Task.CompletedTask, token);
    }

    private static async Task Owned(ClusterFixture fixture, GraphVertexPublicState state,
        Func<RequestCqrsClusterCallers, Task> action, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async callers =>
            {
                await action(callers);
                await GraphVertexPublicAssertions.Models(callers, state, token);
                await GraphVertexPublicAssertions.Originals(callers, state, token);
                return true;
            }, token);
}
