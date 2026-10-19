namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class GraphTests
{
    private const int SharedLevels = 24;

    [Test]
    public async Task AcIs002SharedPredicateDagRetainsIdentityWithoutExpandingEveryPath()
    {
        var value = GraphFixtures.SharedPredicate(SharedLevels);
        var bytes = OrleansSerialization.Serialize(value);
        var restored = OrleansSerialization.Deserialize<KeyLoad.Query.Logical>(bytes);
        await Assert.That(ReferenceEquals(restored.Left, restored.Right)).IsTrue();
        await Assert.That(bytes.Length < SharedLevels * 1_024).IsTrue();
    }

    [Test]
    public async Task AcIs002ActiveReferenceCyclesFailInBothDirections()
    {
        var value = new GraphNode();
        value.Next = value;
        var encoded = GraphFixtures.EncodeUnchecked(value);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<GraphNode>(encoded)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002SemanticDepthFencePreservesItsEdgeAndRejectsTheNextNode()
    {
        var edge = GraphFixtures.Chain(SerializationLimits.SemanticDepth);
        var bytes = OrleansSerialization.Serialize(edge);
        await Assert.That(OrleansSerialization.Deserialize<GraphNode>(bytes)).IsNotNull();
        var excessive = new GraphNode { Next = edge };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(excessive)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var malformed = GraphFixtures.EncodeUnchecked(excessive);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<GraphNode>(malformed)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002MemoizedSubtreeHeightCannotHideADeeperSharedPath()
    {
        var shared = GraphFixtures.Chain(2);
        var edge = new GraphBranches(shared, GraphFixtures.Chain(SerializationLimits.SemanticDepth - 3, shared));
        await Assert.That(OrleansSerialization.Deserialize<GraphBranches>(OrleansSerialization.Serialize(edge))).IsNotNull();
        var excessive = edge with
        {
            Deep = new GraphNode { Next = edge.Deep }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(excessive)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002SharedNullableCollectionCannotSuppressItsRequiredElementContext()
    {
        string[] shared = [null!];
        var value = new GraphCollections(shared, shared);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var bytes = GraphFixtures.EncodeUnchecked(value);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<GraphCollections>(bytes)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var admitted = OrleansSerialization.Deserialize<GraphCollections>(OrleansSerialization.Serialize(value,
            ValidationProfile.PublicInputElements), ValidationProfile.PublicInputElements);
        await Assert.That(admitted.RequiredValues[0]).IsNull();
    }
}
