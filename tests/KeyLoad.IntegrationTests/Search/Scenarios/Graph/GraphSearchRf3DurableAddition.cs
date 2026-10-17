using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchRf3DurableAddition
{
    internal const string Delta = "delta";
    internal const string EdgeId = "edge-second-delta";
    private const string UnknownObservation = "Graph-search leader-loss write observed UnknownWriteOutcome for original command ";
    internal static CommandRequest Create(GraphSearchRf3Scenario scenario) => new(Guid.NewGuid(), scenario.Partition,
        [new PutDocument(GraphSearchRf3Scenario.Documents, Delta, """{"name":"delta"}""", 0),
            new UpsertEdge(GraphSearchRf3Scenario.Graph, EdgeId, scenario.Vertex(GraphSearchRf3Scenario.Projects, GraphSearchRf3Scenario.SecondSeed),
                scenario.Vertex(GraphSearchRf3Scenario.Documents, Delta), GraphSearchRf3Scenario.Label)]);

    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient administrator, CommandRequest command, CancellationToken token)
    {
        var observed = await administrator.CommitAsync(command, token);
        if (!observed.IsSuccess && observed.Problem?.ErrorCode == nameof(ErrorCode.UnknownWriteOutcome))
        {
            TestContext.Current?.Output.WriteLine(UnknownObservation + command.CommandId.ToString("D"));
            token.ThrowIfCancellationRequested();
            observed = await administrator.CommitAsync(command, token);
        }
        var receipt = await McpCallerAssertions.SdkSuccessAsync(observed);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new[]
        { new MutationReceipt("putDocument", GraphSearchRf3Scenario.Documents, Delta, 1),
            new MutationReceipt("upsertEdge", GraphSearchRf3Scenario.Graph, EdgeId, 1) }, CollectionOrdering.Matching);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        return receipt;
    }

    internal static async Task ReplayAsync(KeyLoadClient administrator, CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var replay = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }
}
