using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionReadPayloadTests
{
    private const string MachineKeyFingerprintSource = "execution-fingerprint-source";
    private const string MachineKeyFingerprintTarget = "execution-fingerprint-target";
    private const string MachineKeyPagePartition = "execution-page-partition";
    private const string MachineKeyRequestPartition = "execution-request-partition";
    private const string EdgeId = "execution-edge-read";

    [Test]
    public async Task IncomingRequestRoundTripPreservesVersionTargetGraphAndLimit()
    {
        var partition = GraphCrossPartitionTestSupport.Partition(MachineKeyRequestPartition);
        var target = GraphCrossPartitionTestSupport.Vertex(partition, "execution-request-target");
        var expected = new ReadIncomingGraphEdgesRequest(1, target, "request-graph", 17);

        await GraphCrossPartitionAssertions.RoundTripsAllFields(expected);
    }

    [Test]
    public async Task IncomingRowAndPageRoundTripsPreserveDeliveryAndReadCut()
    {
        var partition = GraphCrossPartitionTestSupport.Partition(MachineKeyPagePartition);
        var from = GraphCrossPartitionTestSupport.Vertex(partition, "execution-page-from");
        var to = GraphCrossPartitionTestSupport.Vertex(partition, "execution-page-to");
        var edge = new EdgeRecord(EdgeId, from, to, "page-label", "{\"field\":true}", 23);
        var row = new GraphIncomingEdgeRow(edge, 29);
        var page = new GraphIncomingEdgesPage(1, ImmutableArray.Create(row), 37,
            "eventual-reverse");

        await GraphCrossPartitionAssertions.RoundTripsAllFields(row);
        await GraphCrossPartitionAssertions.RoundTripsAllFields(page);
    }

    [Test]
    public async Task CapacityAndFingerprintRoundTripsPreserveTheirDistinctFieldLayouts()
    {
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyFingerprintSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyFingerprintTarget);
        var from = GraphCrossPartitionTestSupport.Vertex(source, "execution-fingerprint-from");
        var to = GraphCrossPartitionTestSupport.Vertex(targetPartition, "execution-fingerprint-to");
        var edge = new EdgeRecord(EdgeId, from, to, "fingerprint-label", "{\"n\":7}", 47);
        var capacity = new GraphCrossPartitionCapacity(1,
            GraphCrossPartitionCapacityDirection.ReceiverStates, 11, 127);
        var fingerprint = new GraphCrossPartitionFingerprint(1, source, "fingerprint-graph",
            EdgeId, to, 47, true, edge);

        await GraphCrossPartitionAssertions.RoundTripsAllFields(capacity);
        await GraphCrossPartitionAssertions.RoundTripsAllFields(fingerprint);
        await GraphCrossPartitionAssertions.RoundTripsAllFields(
            GraphCrossPartitionCapacityDirection.PendingIntents);
        await GraphCrossPartitionAssertions.RoundTripsAllFields(
            GraphCrossPartitionCapacityDirection.ReceiverStates);
    }
}
