using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionFingerprintTests
{
    private const string SourceKey = "fingerprint-source";
    private const string TargetKey = "fingerprint-target";
    private const string OtherTargetKey = "fingerprint-other-target";
    private const string Graph = "fingerprint-graph";
    private const string EdgeId = "fingerprint-edge";
    private const string FromId = "fingerprint-from";
    private const string ToId = "fingerprint-to";
    private const string Label = "fingerprint-label";
    private const string Attributes = "{\"weight\":1}";
    private const string ChangedAttributes = "{\"weight\":2}";
    private const long Revision = 7;

    [Test]
    public async Task SeparatelyStoredCanonicalEdgeKeepsTheOriginalDeliveryFingerprint()
    {
        var edge = Edge();
        var persistedEdge = NativeSerialization.Deserialize<EdgeRecord>(NativeSerialization.Serialize(edge));
        await Assert.That(Fingerprint(edge, persistedEdge)).IsEqualTo(Fingerprint(edge, edge));
    }

    [Test]
    public async Task IndependentlyAllocatedEqualReferencesDoNotChangeDeliveryIdentity()
    {
        var first = Edge();
        var independent = Edge();
        await Assert.That(Fingerprint(first, independent)).IsEqualTo(Fingerprint(first, first));
    }

    [Test]
    public async Task FullTargetIdentityAndCanonicalAttributesChangeTheFingerprint()
    {
        var edge = Edge();
        var otherTarget = edge.To with { Partition = edge.To.Partition with { PartitionKey = OtherTargetKey } };
        var movedEdge = edge with { To = otherTarget };
        await Assert.That(Fingerprint(movedEdge, movedEdge)).IsNotEqualTo(Fingerprint(edge, edge));
        await Assert.That(Fingerprint(edge, edge with { AttributesJson = ChangedAttributes }))
            .IsNotEqualTo(Fingerprint(edge, edge));
    }

    private static string Fingerprint(EdgeRecord owner, EdgeRecord snapshot)
        => GraphCrossPartitionRecords.Fingerprint(owner.From.Partition, Graph, owner.Id,
            owner.To, owner.Revision, deleted: false, snapshot);

    private static EdgeRecord Edge()
    {
        var source = GraphCrossPartitionTestSupport.Partition(SourceKey);
        var target = GraphCrossPartitionTestSupport.Partition(TargetKey);
        return new EdgeRecord(EdgeId, GraphCrossPartitionTestSupport.Vertex(source, FromId),
            GraphCrossPartitionTestSupport.Vertex(target, ToId), Label, Attributes, Revision);
    }
}
