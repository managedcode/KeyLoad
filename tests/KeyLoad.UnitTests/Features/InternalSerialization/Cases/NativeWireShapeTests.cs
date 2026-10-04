using System.Collections.Immutable;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeWireShapeTests
{
    private const string ValueKey = "value";
    private const string PairKey = "pair";
    [Test]
    public async Task AcIs002UnverifiedNativeSurrogatesCannotEnterDynamicObjectDecode()
    {
        using var serializer = new NativeSerializerFixture();
        object[] values = [ImmutableList.Create(1f), ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, float>(ValueKey, 1f) }), Tuple.Create(new[] { 1f })];
        foreach (var value in values)
        {
            var bytes = serializer.Encode(value);
            await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<object>(bytes));
            await NativeWireCollectionTests.Reject(() => NativeSerialization.Validate(bytes));
        }
    }

    [Test]
    public async Task AcIs002DerivedNativeCollectionCannotHideUncheckedBaseCount()
    {
        var context = NativeSerializerProviders.Get(typeof(NativeWireDerivedList));
        var bytes = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = new NativeWireDerivedList { 1f } });
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<NativeWireDerivedList>(bytes));
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Validate(bytes, typeof(NativeWireDerivedList)));
    }

    [Test]
    public async Task AcIs002VerifiedNativeCollectionShapesRemainReadable()
    {
        object[] values =
        [
            new List<float> { 1f }, new Dictionary<string, float> { [ValueKey] = 2f },
            new KeyValuePair<string, float[]>(PairKey, [3f]), ImmutableArray.Create(4f),
            new Memory<float>([5f]), new ReadOnlyMemory<float>([6f])
        ];
        var bytes = NativeSerialization.Serialize(values);
        NativeSerialization.Validate(bytes);
        var actual = NativeSerialization.Deserialize<object[]>(bytes);
        await Assert.That(((List<float>)actual[0])[0]).IsEqualTo(1f);
        await Assert.That(((Dictionary<string, float>)actual[1])[ValueKey]).IsEqualTo(2f);
        await Assert.That(((KeyValuePair<string, float[]>)actual[2]).Value[0]).IsEqualTo(3f);
        await Assert.That(((ImmutableArray<float>)actual[3])[0]).IsEqualTo(4f);
        await Assert.That(((Memory<float>)actual[4]).ToArray()[0]).IsEqualTo(5f);
        await Assert.That(((ReadOnlyMemory<float>)actual[5]).ToArray()[0]).IsEqualTo(6f);
    }
}
