using System.Text.Json;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionMutationUnionTests
{
    private const string MachineKeyCollection = "collection";
    private const string MachineKeyDestination = "destination";
    private const string MachineKeyEdgeId = "edgeId";
    private const string MachineKeyExpectedRevision = "expectedRevision";
    private const string MachineKeyGraph = "graph";
    private const string MachineKeyId = "id";
    private const string MachineKeyKind = "kind";
    private const string MachineKeyMutations = "mutations";
    private const string MachineKeyPartition = "partition";
    private const string MachineKeyPartitionKey = "partitionKey";
    private const string MachineKeyResource = "resource";
    private const string MachineKeySourcePartition = "sourcePartition";
    private const string MachineKeyUnionCompleteSource = "union-complete-source";
    private const string MachineKeyUnionCompleteTarget = "union-complete-target";
    private const string MachineKeyUnionSource = "union-source";
    private const string MachineKeyUnionTarget = "union-target";
    private const string Graph = "native-union-graph";
    private const string EdgeId = "native-union-edge";
    private const string ApplyKind = "applyCrossPartitionReverseEdge";
    private const string CompleteKind = "completeCrossPartitionReverseEdge";
    private static readonly Guid CommandId = new("96ad7626-83a8-4550-877b-4c5f393de112");

    [Test]
    public async Task ApplyMutationPreservesInheritedScopeAndLocatorAcrossBothSerializers()
    {
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyUnionSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyUnionTarget);
        var destination = GraphCrossPartitionTestSupport.Vertex(targetPartition, "union-destination");
        var expected = new MutationExpectation(source, Graph, EdgeId, destination, 71);
        await AssertUnionAsync(new ApplyCrossPartitionReverseEdge(source, Graph, EdgeId, destination, 71),
            targetPartition, ApplyKind, expected);
    }

    [Test]
    public async Task CompletionMutationPreservesInheritedScopeAndLocatorAcrossBothSerializers()
    {
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyUnionCompleteSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyUnionCompleteTarget);
        var destination = GraphCrossPartitionTestSupport.Vertex(targetPartition, "union-complete-destination");
        var expected = new MutationExpectation(source, Graph, EdgeId, destination, 89);
        await AssertUnionAsync(new CompleteCrossPartitionReverseEdge(source, Graph, EdgeId,
            destination, 89), source, CompleteKind, expected);
    }

    private static async Task AssertUnionAsync(Mutation mutation, PartitionRef batchPartition,
        string expectedKind, MutationExpectation expected)
    {
        await Assert.That(mutation.Resource).IsEqualTo(expected.Graph);
        var command = new CommandRequest(CommandId, batchPartition, [mutation]);
        await AssertNativeAsync(command, expected);
        await AssertJsonAsync(command, expectedKind, expected);
    }

    private static async Task AssertNativeAsync(CommandRequest command, MutationExpectation expected)
    {
        var actual = NativeSerialization.Deserialize<CommandRequest>(NativeSerialization.Serialize(command));
        await Assert.That(actual.CommandId).IsEqualTo(CommandId);
        await Assert.That(actual.Partition).IsEqualTo(command.Partition);
        await Assert.That(actual.Mutations).HasSingleItem();
        await Assert.That(actual.Mutations[0].GetType()).IsEqualTo(command.Mutations[0].GetType());
        AssertMutation(actual.Mutations[0], expected);
    }

    private static async Task AssertJsonAsync(CommandRequest command, string expectedKind,
        MutationExpectation expected)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(command, JsonDefaults.Options);
        using var document = JsonDocument.Parse(bytes);
        var mutation = document.RootElement.GetProperty(MachineKeyMutations)[0];
        await Assert.That(mutation.GetProperty(MachineKeyKind).GetString()).IsEqualTo(expectedKind);
        await Assert.That(mutation.GetProperty(MachineKeyResource).GetString()).IsEqualTo(expected.Graph);
        await Assert.That(mutation.GetProperty(MachineKeyGraph).GetString()).IsEqualTo(expected.Graph);
        await Assert.That(mutation.GetProperty(MachineKeySourcePartition).GetProperty(MachineKeyPartitionKey).GetString())
            .IsEqualTo(expected.Source.PartitionKey);
        await Assert.That(mutation.GetProperty(MachineKeyDestination).GetProperty(MachineKeyPartition).GetProperty(MachineKeyPartitionKey)
            .GetString()).IsEqualTo(expected.Destination.Partition.PartitionKey);
        await Assert.That(mutation.GetProperty(MachineKeyDestination).GetProperty(MachineKeyCollection).GetString())
            .IsEqualTo(expected.Destination.Collection);
        await Assert.That(mutation.GetProperty(MachineKeyDestination).GetProperty(MachineKeyId).GetString())
            .IsEqualTo(expected.Destination.Id);
        await Assert.That(mutation.GetProperty(MachineKeyEdgeId).GetString()).IsEqualTo(expected.EdgeId);
        await Assert.That(mutation.GetProperty(MachineKeyExpectedRevision).GetInt64()).IsEqualTo(expected.Revision);
        var decoded = JsonSerializer.Deserialize<CommandRequest>(bytes, JsonDefaults.Options);
        await Assert.That(decoded).IsNotNull();
        await Assert.That(decoded!.Mutations).HasSingleItem();
        AssertMutation(decoded.Mutations[0], expected);
    }

    private static void AssertMutation(Mutation mutation, MutationExpectation expected)
    {
        if (mutation.Resource != expected.Graph)
        {
            throw new InvalidOperationException("The inherited mutation resource changed during round trip.");
        }
        if (mutation is ApplyCrossPartitionReverseEdge apply)
        {
            AssertLocator(apply.SourcePartition, apply.Graph, apply.EdgeId, apply.Destination,
                apply.ExpectedRevision, expected);
            return;
        }
        if (mutation is CompleteCrossPartitionReverseEdge complete)
        {
            AssertLocator(complete.SourcePartition, complete.Graph, complete.EdgeId, complete.Destination,
                complete.ExpectedRevision, expected);
            return;
        }
        throw new InvalidOperationException("The native mutation union returned an unexpected graph mutation.");
    }

    private static void AssertLocator(PartitionRef source, string graph, string edgeId,
        EntityRef destination, long revision, MutationExpectation expected)
    {
        if (source != expected.Source || graph != expected.Graph || edgeId != expected.EdgeId
            || destination != expected.Destination || revision != expected.Revision)
        {
            throw new InvalidOperationException("The native graph mutation locator changed during round trip.");
        }
    }

    private sealed record MutationExpectation(PartitionRef Source, string Graph, string EdgeId,
        EntityRef Destination, long Revision);
}
