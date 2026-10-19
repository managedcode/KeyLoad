namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class WireCollectionTests
{
    [Test]
    [Arguments(2u, 1, false)]
    [Arguments(1u, 0, false)]
    [Arguments(1u, 2, false)]
    [Arguments(1u, 1, true)]
    [Arguments(uint.MaxValue, 1, false)]
    public async Task AcIs002ArrayCountMustDescribeEveryElement(uint count, int items, bool duplicate)
    {
        var bytes = WireFixture.Collection(typeof(float[]), count, items, duplicate);
        await Reject(() => OrleansSerialization.Deserialize<float[]>(bytes));
        await Reject(() => OrleansSerialization.Validate(bytes));
    }

    [Test]
    [Arguments(2u, 1)]
    [Arguments(1u, 0)]
    [Arguments(1u, 2)]
    public async Task AcIs002ListCountMustDescribeEveryElement(uint count, int items)
    {
        var bytes = WireFixture.Collection(typeof(List<float>), count, items);
        await Reject(() => OrleansSerialization.Deserialize<List<float>>(bytes));
        await Reject(() => OrleansSerialization.Validate(bytes));
    }

    [Test]
    [Arguments(1u, 1)]
    [Arguments(1u, 0)]
    [Arguments(2u, 2)]
    [Arguments(1u, 4)]
    public async Task AcIs002DictionaryCountMustDescribeCompletePairs(uint count, int fields)
    {
        var bytes = WireFixture.Collection(typeof(Dictionary<string, string>), count, fields);
        await Reject(() => OrleansSerialization.Deserialize<Dictionary<string, string>>(bytes));
        await Reject(() => OrleansSerialization.Validate(bytes));
    }

    [Test]
    public async Task AcIs002DuplicateDictionaryCountIsRejected()
    {
        var bytes = WireFixture.Collection(typeof(Dictionary<string, string>), 1, 2, true);
        await Reject(() => OrleansSerialization.Deserialize<Dictionary<string, string>>(bytes));
        await Reject(() => OrleansSerialization.Validate(bytes));
    }

    [Test]
    public async Task AcIs002CollectionItemsRequireTheirSingleCountField()
    {
        Type[] types = [typeof(float[]), typeof(List<float>), typeof(Dictionary<string, string>)];
        foreach (var type in types)
        {
            var bytes = WireFixture.Collection(type, 1, 2, omitCount: true);
            await Reject(() => OrleansSerialization.Deserialize<object>(bytes));
            await Reject(() => OrleansSerialization.Validate(bytes));
        }
        var repeated = WireFixture.Collection(typeof(List<float>), 1, 1, true);
        await Reject(() => OrleansSerialization.Deserialize<List<float>>(repeated));
        await Reject(() => OrleansSerialization.Validate(repeated));
    }

    [Test]
    public async Task AcIs002OpaqueEvolutionFieldCannotBecomeTypedArrayByReference()
    {
        var bytes = WireOpaqueFixture.ReferencedVector();
        await Reject(() => OrleansSerialization.Deserialize<Mutation>(bytes));
        await Reject(() => OrleansSerialization.Validate(bytes));
    }

    [Test]
    public async Task AcIs002OrdinaryUnknownFieldsRemainSkippable()
    {
        var bytes = WireOpaqueFixture.ReferencedVector(false);
        OrleansSerialization.Validate(bytes);
        var value = (PutVector)OrleansSerialization.Deserialize<Mutation>(bytes);
        await Assert.That(value.Values.ToArray()).IsEquivalentTo(new float[] { 1f, 2f }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcIs002ImplicitImmutableVectorArrayCannotHaveDefaultSuffix()
    {
        await Reject(() => OrleansSerialization.Deserialize<VectorRecord>(WireFixture.NestedVector(typeof(VectorRecord))));
        await Reject(() => OrleansSerialization.Deserialize<Mutation>(WireFixture.NestedVector(typeof(PutVector))));
        await Reject(() => OrleansSerialization.Validate(WireFixture.NestedVector(typeof(VectorRecord))));
        await Reject(() => OrleansSerialization.Validate(WireFixture.NestedVector(typeof(PutVector))));
        await Reject(() => OrleansSerialization.Deserialize<PrincipalRecord>(WireFixture.PrincipalBodyArray()));
        await Reject(() => OrleansSerialization.Validate(WireFixture.PrincipalBodyArray()));
    }

    [Test]
    public async Task AcIs002EmptyAndRepeatedCollectionsRemainReadable()
    {
        object[] values = [Array.Empty<float>(), new List<float>(), new Dictionary<string, string>(StringComparer.Ordinal)];
        var array = new[] { 1f, 2f };
        object[] shared = [array, array];
        var empty = OrleansSerialization.Deserialize<object[]>(OrleansSerialization.Serialize(values));
        var repeated = OrleansSerialization.Deserialize<object[]>(OrleansSerialization.Serialize(shared));
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
        await Assert.That(failure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
    }
}
