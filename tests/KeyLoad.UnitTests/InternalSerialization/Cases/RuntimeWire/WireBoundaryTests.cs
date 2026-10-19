using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class WireBoundaryTests
{
    [Test]
    public async Task AcIs002WireDepthFenceAcceptsLimitAndRejectsNextLevel()
    {
        // The envelope adds one level to the independently authored tag tree.
        OrleansSerialization.Validate(WireFixture.Tags(SerializationLimits.WireDepth - 1));
        await WireCollectionTests.Reject(() => OrleansSerialization.Validate(WireFixture.Tags(SerializationLimits.WireDepth)));
        await WireCollectionTests.Reject(() => OrleansSerialization.Deserialize<OutboxHead>(WireFixture.Tags(SerializationLimits.WireDepth)));
    }

    [Test]
    [Arguments(2u)]
    [Arguments(4u)]
    [Arguments(uint.MaxValue)]
    public async Task AcIs002ReferenceCannotTargetScalarSelfSlotOrFutureField(uint target)
    {
        var bytes = WireFixture.Tags(1, target);
        await WireCollectionTests.Reject(() => OrleansSerialization.Validate(bytes));
        await WireCollectionTests.Reject(() => OrleansSerialization.Deserialize<OutboxHead>(bytes));
    }

    [Test]
    public async Task AcIs002WrongDynamicRootIsRejectedBeforeItsDeclaredCollection()
    {
        var bytes = WireFixture.Collection(typeof(float[]), uint.MaxValue, 1);
        await WireCollectionTests.Reject(() => OrleansSerialization.Deserialize<OutboxHead>(bytes));
    }

    [Test]
    public async Task AcIs002AssignableMutationAndImmutableVectorRootsRemainReadable()
    {
        var space = new VectorSpace("space", 2, DistanceMetric.Cosine, "model", "1");
        var value = new PutVector("vectors", "doc", "embedding", ImmutableArray.Create(1f, 2f), space, 1);
        var bytes = OrleansSerialization.Serialize<Mutation>(value);
        OrleansSerialization.Validate(bytes, typeof(Mutation));
        var actual = (PutVector)OrleansSerialization.Deserialize<Mutation>(bytes);
        await Assert.That(actual.Values.ToArray()).IsEquivalentTo(value.Values.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcIs002NullableByteMemoryRetainsSharedSourceAndNullDistinction()
    {
        var source = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte>?[] values = [new ReadOnlyMemory<byte>(source), null, new ReadOnlyMemory<byte>(source)];
        var actual = OrleansSerialization.Deserialize<ReadOnlyMemory<byte>?[]>(OrleansSerialization.Serialize(values));
        Array.Clear(source);
        await Assert.That(actual[0]!.Value.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(actual[1]).IsNull();
        await Assert.That(actual[2]!.Value.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
