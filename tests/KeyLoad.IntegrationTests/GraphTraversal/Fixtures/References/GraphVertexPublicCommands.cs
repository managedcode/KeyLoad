using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphVertexPublicCommands
{
    private const string PutKind = "putDocument";
    private const string UpsertKind = "upsertEdge";
    private const string DeleteKind = "deleteEdge";

    internal static CommandRequest Command(GraphVertexPublicState state, params Mutation[] mutations)
        => new(Guid.NewGuid(), state.Seed.TargetPartition, [.. mutations]);

    internal static UpsertEdge Upsert(GraphVertexPublicState state, EntityRef target, long expected)
        => new(state.Seed.Graph, GraphVertexPublicState.EdgeId, state.Seed.LocalSource, target,
            GraphVertexPublicState.Label, GraphVertexPublicState.Json, expected);

    internal static async Task Commit(RequestCqrsClusterCallers callers, GraphVertexPublicState state,
        CommandRequest command, MutationReceipt[] expected, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations.SequenceEqual(expected)).IsTrue();
        state.Original.Add((command, receipt));
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
    }

    internal static async Task Refuse(RequestCqrsClusterCallers callers, GraphVertexPublicState state,
        CommandRequest command, ErrorCode error, CancellationToken token)
    {
        var sdk = await callers.Sdk.CommitAsync(command, token);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(sdk, error);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token), error, dispatched: true);
        state.Refusals.Add((command, error));
        await GraphVertexPublicAssertions.Originals(callers, state, token);
        await GraphVertexPublicAssertions.Models(callers, state, token);
    }

    internal static MutationReceipt HealthyReceipt(GraphVertexPublicState state)
        => new(UpsertKind, state.Seed.Graph, GraphVertexPublicState.HealthyId, 1L);

    internal static MutationReceipt PutReceipt(long revision)
        => new(PutKind, GraphIncomingClusterScenario.Collection, GraphVertexPublicState.NewId, revision);

    internal static MutationReceipt EdgeReceipt(GraphVertexPublicState state, long revision, bool remove = false)
        => new(remove ? DeleteKind : UpsertKind, state.Seed.Graph, GraphVertexPublicState.EdgeId, revision);
}
