using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeGraphTests
{
    private const int SharedLevels = 24;

    [Test]
    public async Task AcIs002SharedPredicateDagRetainsIdentityWithoutExpandingEveryPath()
    {
        var value = NativeGraphFixtures.SharedPredicate(SharedLevels);
        var bytes = NativeSerialization.Serialize(value);
        var restored = NativeSerialization.Deserialize<KeyLoad.Query.Logical>(bytes);
        await Assert.That(ReferenceEquals(restored.Left, restored.Right)).IsTrue();
        await Assert.That(bytes.Length < SharedLevels * 1_024).IsTrue();
    }

    [Test]
    public async Task AcIs002ActiveReferenceCyclesFailInBothDirections()
    {
        var value = new NativeGraphNode();
        value.Next = value;
        var encoded = NativeGraphFixtures.EncodeUnchecked(value);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<NativeGraphNode>(encoded)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002SemanticDepthFencePreservesItsEdgeAndRejectsTheNextNode()
    {
        var edge = NativeGraphFixtures.Chain(NativeSerializationLimits.SemanticDepth);
        var bytes = NativeSerialization.Serialize(edge);
        await Assert.That(NativeSerialization.Deserialize<NativeGraphNode>(bytes)).IsNotNull();
        var excessive = new NativeGraphNode { Next = edge };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(excessive)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var malformed = NativeGraphFixtures.EncodeUnchecked(excessive);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<NativeGraphNode>(malformed)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002MemoizedSubtreeHeightCannotHideADeeperSharedPath()
    {
        var shared = NativeGraphFixtures.Chain(2);
        var edge = new NativeGraphBranches(shared, NativeGraphFixtures.Chain(NativeSerializationLimits.SemanticDepth - 3, shared));
        await Assert.That(NativeSerialization.Deserialize<NativeGraphBranches>(NativeSerialization.Serialize(edge))).IsNotNull();
        var excessive = edge with { Deep = new NativeGraphNode { Next = edge.Deep } };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(excessive)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002SharedNullableCollectionCannotSuppressItsRequiredElementContext()
    {
        string[] shared = [null!];
        var value = new NativeGraphCollections(shared, shared);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var bytes = NativeGraphFixtures.EncodeUnchecked(value);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<NativeGraphCollections>(bytes)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var admitted = NativeSerialization.Deserialize<NativeGraphCollections>(NativeSerialization.Serialize(value,
            NativeValidationProfile.PublicInputElements), NativeValidationProfile.PublicInputElements);
        await Assert.That(admitted.RequiredValues[0]).IsNull();
    }
}
