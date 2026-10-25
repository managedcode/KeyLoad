using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.ClientApi;
using KeyLoad.FunctionalTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

/// <summary>AC-COMP-007: composition retains canonical typed execution and public transport contracts.</summary>
internal sealed class DatabaseCompositionContractTests
{
    private const string Queue = "agent-jobs";
    private const string Graph = "knowledge";
    private const string Collection = "entities";
    private const string First = "first";
    private const string Second = "second";
    private const string Prefix = "derived-";
    private const string Label = "related";

    [Test]
    public async Task AC_COMP_007_PublicAndCommandRoundTripsPreserveConcreteCompositionTypes()
    {
        var command = Command();
        var json = JsonDefaults.Deserialize<CommandRequest>(JsonSerializer.Serialize(command, JsonDefaults.Options));
        var execution = OrleansSerialization.Deserialize<CommandRequest>(OrleansSerialization.Serialize(command));
        foreach (var restored in new[] { json, execution })
        {
            await Assert.That(restored.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(restored.Partition).IsEqualTo(command.Partition);
            await Assert.That(restored.Mutations[0]).IsTypeOf<QueueToGraph>();
            await Assert.That(restored.Mutations[1]).IsTypeOf<GraphToQueueMutation>();
            await Assert.That(JsonDefaults.Serialize(restored).AsSpan().SequenceEqual(JsonDefaults.Serialize(command))).IsTrue();
        }
        var link = new QueueGraphLink(new(command.Partition, Collection, First),
            new(command.Partition, Collection, Second), Label);
        await Assert.That(OrleansSerialization.Deserialize<QueueGraphLink>(OrleansSerialization.Serialize(link))).IsEqualTo(link);
    }

    [Test]
    public async Task AC_COMP_007_SqlCallKeepsOneCanonicalBatchAndStableCommandIdentity()
    {
        var command = Command();
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(command, JsonDefaults.Options)
        };
        var compiled = SqlOperationTestData.Compile(SqlOperationTestData.Call(McpCatalogExpectations.DocumentsCommit, arguments));
        var execution = SqlOperationTestData.Find(McpCatalogExpectations.DocumentsCommit).Decode(arguments);
        await SqlOperationTestData.Same(compiled, execution);
        await Assert.That(compiled.CommandKind).IsEqualTo(OperationKind.Batch);
        await Assert.That(compiled.ReadKind).IsNull();
        await Assert.That(compiled.CommandId).IsEqualTo(command.CommandId);
        var restored = GrainPayload.Read<CommandRequest>(compiled.Payload);
        await Assert.That(restored.Mutations[0]).IsTypeOf<QueueToGraph>();
        await Assert.That(restored.Mutations[1]).IsTypeOf<GraphToQueueMutation>();
    }

    private static CommandRequest Command() => new(Guid.NewGuid(), McpCanonicalTestData.Partition,
        [new QueueToGraph(Graph, Queue, Prefix), new GraphToQueueMutation(Queue, Graph,
            new(McpCanonicalTestData.Partition, Collection, First), Prefix)]);
}
