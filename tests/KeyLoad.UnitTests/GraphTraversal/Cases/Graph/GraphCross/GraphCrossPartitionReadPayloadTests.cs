using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionReadPayloadTests
{
    private const string MachineKeyNativeFingerprintSource = "native-fingerprint-source";
    private const string MachineKeyNativeFingerprintTarget = "native-fingerprint-target";
    private const string MachineKeyNativePagePartition = "native-page-partition";
    private const string MachineKeyNativeRequestPartition = "native-request-partition";
    private const string EdgeId = "native-edge-read";

    [Test]
    public async Task IncomingRequestRoundTripPreservesVersionTargetGraphAndLimit()
    {
        var partition = GraphCrossPartitionTestSupport.Partition(MachineKeyNativeRequestPartition);
        var target = GraphCrossPartitionTestSupport.Vertex(partition, "native-request-target");
        var expected = new ReadIncomingGraphEdgesRequestV1(1, target, "request-graph", 17);

        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(expected);
    }

    [Test]
    public async Task IncomingRowAndPageRoundTripsPreserveDeliveryAndReadCut()
    {
        var partition = GraphCrossPartitionTestSupport.Partition(MachineKeyNativePagePartition);
        var from = GraphCrossPartitionTestSupport.Vertex(partition, "native-page-from");
        var to = GraphCrossPartitionTestSupport.Vertex(partition, "native-page-to");
        var edge = new EdgeRecord(EdgeId, from, to, "page-label", "{\"field\":true}", 23);
        var row = new GraphIncomingEdgeRowV1(edge, 29);
        var page = new GraphIncomingEdgesPageV1(1, ImmutableArray.Create(row), 37,
            "eventual-reverse.v1");

        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(row);
        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(page);
    }

    [Test]
    public async Task CapacityAndFingerprintRoundTripsPreserveTheirDistinctFieldLayouts()
    {
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyNativeFingerprintSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyNativeFingerprintTarget);
        var from = GraphCrossPartitionTestSupport.Vertex(source, "native-fingerprint-from");
        var to = GraphCrossPartitionTestSupport.Vertex(targetPartition, "native-fingerprint-to");
        var edge = new EdgeRecord(EdgeId, from, to, "fingerprint-label", "{\"n\":7}", 47);
        var capacity = new GraphCrossPartitionCapacityV1(1,
            GraphCrossPartitionCapacityDirection.ReceiverStates, 11, 127);
        var fingerprint = new GraphCrossPartitionFingerprintV1(1, source, "fingerprint-graph",
            EdgeId, to, 47, true, edge);

        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(capacity);
        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(fingerprint);
        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(
            GraphCrossPartitionCapacityDirection.PendingIntents);
        await GraphCrossPartitionNativeAssertions.RoundTripsAllFields(
            GraphCrossPartitionCapacityDirection.ReceiverStates);
    }
}
