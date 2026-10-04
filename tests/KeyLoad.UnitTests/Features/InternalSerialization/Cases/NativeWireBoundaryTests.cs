using System.Collections.Immutable;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeWireBoundaryTests
{
    [Test]
    public async Task AcIs002WireDepthFenceAcceptsLimitAndRejectsNextLevel()
    {
        // The envelope adds one level to the independently authored tag tree.
        NativeSerialization.Validate(NativeWireFixture.Tags(NativeSerializationLimits.WireDepth - 1));
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Validate(NativeWireFixture.Tags(NativeSerializationLimits.WireDepth)));
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<OutboxHead>(NativeWireFixture.Tags(NativeSerializationLimits.WireDepth)));
    }

    [Test]
    [Arguments(2u)]
    [Arguments(4u)]
    [Arguments(uint.MaxValue)]
    public async Task AcIs002ReferenceCannotTargetScalarSelfSlotOrFutureField(uint target)
    {
        var bytes = NativeWireFixture.Tags(1, target);
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Validate(bytes));
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<OutboxHead>(bytes));
    }

    [Test]
    public async Task AcIs002WrongDynamicRootIsRejectedBeforeItsDeclaredCollection()
    {
        var bytes = NativeWireFixture.Collection(typeof(float[]), uint.MaxValue, 1);
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<OutboxHead>(bytes));
    }

    [Test]
    public async Task AcIs002AssignableMutationAndImmutableVectorRootsRemainReadable()
    {
        var space = new VectorSpace("space", 2, DistanceMetric.Cosine, "model", "1");
        var value = new PutVector("vectors", "doc", "embedding", ImmutableArray.Create(1f, 2f), space, 1);
        var bytes = NativeSerialization.Serialize<Mutation>(value);
        NativeSerialization.Validate(bytes, typeof(Mutation));
        var actual = (PutVector)NativeSerialization.Deserialize<Mutation>(bytes);
        await Assert.That(actual.Values.ToArray()).IsEquivalentTo(value.Values.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcIs002NullableByteMemoryRetainsSharedSourceAndNullDistinction()
    {
        var source = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte>?[] values = [new ReadOnlyMemory<byte>(source), null, new ReadOnlyMemory<byte>(source)];
        var actual = NativeSerialization.Deserialize<ReadOnlyMemory<byte>?[]>(NativeSerialization.Serialize(values));
        Array.Clear(source);
        await Assert.That(actual[0]!.Value.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(actual[1]).IsNull();
        await Assert.That(actual[2]!.Value.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
