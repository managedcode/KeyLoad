using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class WireShapeTests
{
    private const string ValueKey = "value";
    private const string PairKey = "pair";
    [Test]
    public async Task AcIs002UnverifiedSurrogatesCannotEnterDynamicObjectDecode()
    {
        using var serializer = new SerializerFixture();
        object[] values = [ImmutableList.Create(1f), Tuple.Create(new[] { 1f })];
        foreach (var value in values)
        {
            var bytes = serializer.Encode(value);
            await WireCollectionTests.Reject(() => OrleansSerialization.Deserialize<object>(bytes));
            await WireCollectionTests.Reject(() => OrleansSerialization.Validate(bytes));
        }
    }

    [Test]
    public async Task AcIs002DerivedCollectionCannotHideUncheckedBaseCount()
    {
        var context = SerializerProviders.Get(typeof(WireDerivedList));
        var bytes = context.Serializer.SerializeToArray(new RuntimePayload { Version = PayloadVersion.Current, Value = new WireDerivedList { 1f } });
        await WireCollectionTests.Reject(() => OrleansSerialization.Deserialize<WireDerivedList>(bytes));
        await WireCollectionTests.Reject(() => OrleansSerialization.Validate(bytes, typeof(WireDerivedList)));
    }

    [Test]
    public async Task AcIs002VerifiedCollectionShapesRemainReadable()
    {
        object[] values =
        [
            new List<float> { 1f }, new Dictionary<string, float> { [ValueKey] = 2f },
            new KeyValuePair<string, float[]>(PairKey, [3f]), ImmutableArray.Create(4f),
            new Memory<float>([5f]), new ReadOnlyMemory<float>([6f])
        ];
        var bytes = OrleansSerialization.Serialize(values);
        OrleansSerialization.Validate(bytes);
        var actual = OrleansSerialization.Deserialize<object[]>(bytes);
        await Assert.That(((List<float>)actual[0])[0]).IsEqualTo(1f);
        await Assert.That(((Dictionary<string, float>)actual[1])[ValueKey]).IsEqualTo(2f);
        await Assert.That(((KeyValuePair<string, float[]>)actual[2]).Value[0]).IsEqualTo(3f);
        await Assert.That(((ImmutableArray<float>)actual[3])[0]).IsEqualTo(4f);
        await Assert.That(((Memory<float>)actual[4]).ToArray()[0]).IsEqualTo(5f);
        await Assert.That(((ReadOnlyMemory<float>)actual[5]).ToArray()[0]).IsEqualTo(6f);
    }
}
