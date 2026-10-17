using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeWireCollectionTests
{
    [Test]
    [Arguments(2u, 1, false)]
    [Arguments(1u, 0, false)]
    [Arguments(1u, 2, false)]
    [Arguments(1u, 1, true)]
    [Arguments(uint.MaxValue, 1, false)]
    public async Task AcIs002ArrayCountMustDescribeEveryElement(uint count, int items, bool duplicate)
    {
        var bytes = NativeWireFixture.Collection(typeof(float[]), count, items, duplicate);
        await Reject(() => NativeSerialization.Deserialize<float[]>(bytes));
        await Reject(() => NativeSerialization.Validate(bytes));
    }

    [Test]
    [Arguments(2u, 1)]
    [Arguments(1u, 0)]
    [Arguments(1u, 2)]
    public async Task AcIs002ListCountMustDescribeEveryElement(uint count, int items)
    {
        var bytes = NativeWireFixture.Collection(typeof(List<float>), count, items);
        await Reject(() => NativeSerialization.Deserialize<List<float>>(bytes));
        await Reject(() => NativeSerialization.Validate(bytes));
    }

    [Test]
    [Arguments(1u, 1)]
    [Arguments(1u, 0)]
    [Arguments(2u, 2)]
    [Arguments(1u, 4)]
    public async Task AcIs002DictionaryCountMustDescribeCompletePairs(uint count, int fields)
    {
        var bytes = NativeWireFixture.Collection(typeof(Dictionary<string, string>), count, fields);
        await Reject(() => NativeSerialization.Deserialize<Dictionary<string, string>>(bytes));
        await Reject(() => NativeSerialization.Validate(bytes));
    }

    [Test]
    public async Task AcIs002DuplicateDictionaryCountIsRejected()
    {
        var bytes = NativeWireFixture.Collection(typeof(Dictionary<string, string>), 1, 2, true);
        await Reject(() => NativeSerialization.Deserialize<Dictionary<string, string>>(bytes));
        await Reject(() => NativeSerialization.Validate(bytes));
    }

    [Test]
    public async Task AcIs002CollectionItemsRequireTheirSingleCountField()
    {
        Type[] types = [typeof(float[]), typeof(List<float>), typeof(Dictionary<string, string>)];
        foreach (var type in types)
        {
            var bytes = NativeWireFixture.Collection(type, 1, 2, omitCount: true);
            await Reject(() => NativeSerialization.Deserialize<object>(bytes));
            await Reject(() => NativeSerialization.Validate(bytes));
        }
        var repeated = NativeWireFixture.Collection(typeof(List<float>), 1, 1, true);
        await Reject(() => NativeSerialization.Deserialize<List<float>>(repeated));
        await Reject(() => NativeSerialization.Validate(repeated));
    }

    [Test]
    public async Task AcIs002OpaqueEvolutionFieldCannotBecomeTypedArrayByReference()
    {
        var bytes = NativeWireOpaqueFixture.ReferencedVector();
        await Reject(() => NativeSerialization.Deserialize<Mutation>(bytes));
        await Reject(() => NativeSerialization.Validate(bytes));
    }

    [Test]
    public async Task AcIs002OrdinaryUnknownFieldsRemainSkippable()
    {
        var bytes = NativeWireOpaqueFixture.ReferencedVector(false);
        NativeSerialization.Validate(bytes);
        var value = (PutVector)NativeSerialization.Deserialize<Mutation>(bytes);
        await Assert.That(value.Values.ToArray()).IsEquivalentTo(new float[] { 1f, 2f }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcIs002ImplicitImmutableVectorArrayCannotHaveDefaultSuffix()
    {
        await Reject(() => NativeSerialization.Deserialize<VectorRecord>(NativeWireFixture.NestedVector(typeof(VectorRecord))));
        await Reject(() => NativeSerialization.Deserialize<Mutation>(NativeWireFixture.NestedVector(typeof(PutVector))));
        await Reject(() => NativeSerialization.Validate(NativeWireFixture.NestedVector(typeof(VectorRecord))));
        await Reject(() => NativeSerialization.Validate(NativeWireFixture.NestedVector(typeof(PutVector))));
        await Reject(() => NativeSerialization.Deserialize<PrincipalRecord>(NativeWireFixture.PrincipalBodyArray()));
        await Reject(() => NativeSerialization.Validate(NativeWireFixture.PrincipalBodyArray()));
    }

    [Test]
    public async Task AcIs002NativeEmptyAndRepeatedCollectionsRemainReadable()
    {
        object[] values = [Array.Empty<float>(), new List<float>(), new Dictionary<string, string>(StringComparer.Ordinal)];
        var array = new[] { 1f, 2f };
        object[] shared = [array, array];
        var empty = NativeSerialization.Deserialize<object[]>(NativeSerialization.Serialize(values));
        var repeated = NativeSerialization.Deserialize<object[]>(NativeSerialization.Serialize(shared));
        await Assert.That(empty.Length).IsEqualTo(3);
        await Assert.That(((Dictionary<string, string>)empty[2]).Comparer == StringComparer.Ordinal).IsTrue();
        await Assert.That(ReferenceEquals(repeated[0], repeated[1])).IsTrue();
    }

    internal static async Task Reject(Action action)
    {
        KeyLoadException? failure = null;
        try
        {
            action();
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
    }
}
