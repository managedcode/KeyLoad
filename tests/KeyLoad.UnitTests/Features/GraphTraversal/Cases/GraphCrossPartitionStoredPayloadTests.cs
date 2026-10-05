namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionStoredPayloadTests
{
    private const string EdgeId = "native-edge-stored";
    private const string Fingerprint = "fingerprint-0123456789abcdef";

    [Test]
    public async Task OwnerVersionRoundTripPreservesIndependentRevisionAndDeletedFields()
    {
        var expected = new GraphEdgeOwnerVersionV1(1, 31, true);

        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(expected);
    }

    [Test]
    public async Task DeliveryIntentRoundTripPreservesEveryScopedFieldAndSnapshot()
    {
        var source = GraphCrossPartitionTestSupport.Partition("native-intent-source");
        var targetPartition = GraphCrossPartitionTestSupport.Partition("native-intent-target");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "native-from");
        var destination = GraphCrossPartitionTestSupport.Vertex(targetPartition, "native-to");
        var edge = new EdgeRecord(EdgeId, from, destination, "intent-label", "{\"v\":1}", 41);
        var expected = new GraphCrossPartitionDeliveryIntentV1(1, source,
            "intent-graph", EdgeId, destination, 41, true, edge, "intent-principal", 43, Fingerprint);

        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(expected);
    }

    [Test]
    public async Task ReceiverStateRoundTripPreservesHighWaterFieldsWithoutIdCollision()
    {
        var source = GraphCrossPartitionTestSupport.Partition("native-receiver-source");
        var targetPartition = GraphCrossPartitionTestSupport.Partition("native-receiver-target");
        var destination = GraphCrossPartitionTestSupport.Vertex(targetPartition, "native-receiver-to");
        var expected = new GraphCrossPartitionReceiverStateV1(1, source, "receiver-graph",
            EdgeId, destination, 53, true, Fingerprint);

        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(expected);
    }
}
