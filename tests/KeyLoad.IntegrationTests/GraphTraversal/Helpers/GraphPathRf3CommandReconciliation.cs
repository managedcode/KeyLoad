using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRf3CommandReconciliation
{
    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient administrator, CommandRequest command,
        CancellationToken cancellationToken)
    {
        var observed = await administrator.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        if (!observed.IsSuccess && observed.Problem?.ErrorCode == nameof(ErrorCode.UnknownWriteOutcome))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observed = await administrator.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        }
        var receipt = await McpCallerAssertions.SdkSuccessAsync(observed).ConfigureAwait(false);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        var edge = (UpsertEdge)command.Mutations.Single();
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("upsertEdge", edge.Graph, edge.EdgeId, 1));
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        return receipt;
    }

    internal static Task AssertShortcutAsync(GraphShortestPathResult result, CommandRequest command)
    {
        var edge = (UpsertEdge)command.Mutations.Single();
        return AssertShortcutAsync(result, new EdgeRecord(edge.EdgeId, edge.From, edge.To, edge.Label, "{}", 1));
    }

    internal static Task AssertShortcutAsync(GraphShortestPathResult result, GraphPathRf3Seed seed, string edgeId)
        => AssertShortcutAsync(result, new EdgeRecord(edgeId, seed.Source, seed.Target, GraphPathRf3Scenario.Label, "{}", 1));

    private static async Task AssertShortcutAsync(GraphShortestPathResult result, EdgeRecord expected)
    {
        await Assert.That(result.Edges.Length).IsEqualTo(1);
        await Assert.That(result.Edges[0]).IsEqualTo(expected);
    }
}
