using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Search;

internal static class GraphSearchClusterDurableAddition
{
    internal const string Delta = "delta";
    internal const string EdgeId = "edge-second-delta";
    private const string UnknownObservation = "Graph-search silo-restart write observed UnknownWriteOutcome for original command ";
    internal static CommandRequest Create(GraphSearchClusterScenario scenario) => new(Guid.NewGuid(), scenario.Partition,
        [new PutDocument(GraphSearchClusterScenario.Documents, Delta, """{"name":"delta"}""", 0),
            new UpsertEdge(GraphSearchClusterScenario.Graph, EdgeId, scenario.Vertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.SecondSeed),
                scenario.Vertex(GraphSearchClusterScenario.Documents, Delta), GraphSearchClusterScenario.Label)]);

    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient administrator, CommandRequest command, CancellationToken token)
    {
        var observed = await administrator.CommitAsync(command, token);
        if (observed.IsFailed && observed.Problem.ErrorCode == nameof(ErrorCode.UnknownWriteOutcome))
        {
            TestContext.Current?.Output.WriteLine(UnknownObservation + command.CommandId.ToString("D"));
            token.ThrowIfCancellationRequested();
            observed = await administrator.CommitAsync(command, token);
        }
        var receipt = await McpCallerAssertions.SdkSuccessAsync(observed);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new[]
        { new MutationReceipt("putDocument", GraphSearchClusterScenario.Documents, Delta, 1),
            new MutationReceipt("upsertEdge", GraphSearchClusterScenario.Graph, EdgeId, 1) }, CollectionOrdering.Matching);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        return receipt;
    }

    internal static async Task ReplayAsync(KeyLoadClient administrator, CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var replay = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }
}
