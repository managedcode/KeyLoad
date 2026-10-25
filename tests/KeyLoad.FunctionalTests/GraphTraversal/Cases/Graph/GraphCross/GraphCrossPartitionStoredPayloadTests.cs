namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphCrossPartitionStoredPayloadTests
{
    private const string MachineKeyIntentSource = "execution-intent-source";
    private const string MachineKeyIntentTarget = "execution-intent-target";
    private const string MachineKeyReceiverSource = "execution-receiver-source";
    private const string MachineKeyReceiverTarget = "execution-receiver-target";
    private const string EdgeId = "execution-edge-stored";
    private const string Fingerprint = "fingerprint-0123456789abcdef";

    [Test]
    public async Task OwnerVersionRoundTripPreservesIndependentRevisionAndDeletedFields()
    {
        var expected = new GraphEdgeOwnerVersion(1, 31, true);

        await GraphCrossPartitionAssertions.RoundTripsAllFields(expected);
    }

    [Test]
    public async Task DeliveryIntentRoundTripPreservesEveryScopedFieldAndSnapshot()
    {
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyIntentSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyIntentTarget);
        var from = GraphCrossPartitionTestSupport.Vertex(source, "execution-from");
        var destination = GraphCrossPartitionTestSupport.Vertex(targetPartition, "execution-to");
        var edge = new EdgeRecord(EdgeId, from, destination, "intent-label", "{\"v\":1}", 41);
        var expected = new GraphCrossPartitionDeliveryIntent(1, source,
            "intent-graph", EdgeId, destination, 41, true, edge, "intent-principal", 43, Fingerprint);

        await GraphCrossPartitionAssertions.RoundTripsAllFields(expected);
    }

    [Test]
    public async Task ReceiverStateRoundTripPreservesHighWaterFieldsWithoutIdCollision()
    {
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyReceiverSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyReceiverTarget);
        var destination = GraphCrossPartitionTestSupport.Vertex(targetPartition, "execution-receiver-to");
        var expected = new GraphCrossPartitionReceiverState(1, source, "receiver-graph",
            EdgeId, destination, 53, true, Fingerprint);

        await GraphCrossPartitionAssertions.RoundTripsAllFields(expected);
    }
}
