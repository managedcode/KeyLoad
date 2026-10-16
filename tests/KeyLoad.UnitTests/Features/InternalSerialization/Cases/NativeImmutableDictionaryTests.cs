using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeImmutableDictionaryTests
{
    private const string MapKey = "key";
    private const string MapValue = "value";

    [Test]
    public async Task VerifiedImmutableMapPreservesKeysValuesComparersAndReferenceIdentity()
    {
        var map = ImmutableDictionary<string, string>.Empty
            .WithComparers(StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase).Add(MapKey, "VALUE");
        var metrics = ImmutableDictionary<string, double>.Empty.Add(MapValue, 4);
        object[] original = [map, map, metrics, ImmutableDictionary<string, long>.Empty];
        var bytes = NativeSerialization.Serialize(original);
        NativeSerialization.Validate(bytes);
        var actual = NativeSerialization.Deserialize<object[]>(bytes);
        var restored = (ImmutableDictionary<string, string>)actual[0];
        await Assert.That(restored.Count).IsEqualTo(1);
        await Assert.That(restored[MapKey]).IsEqualTo("VALUE");
        await Assert.That(restored.KeyComparer == StringComparer.Ordinal).IsTrue();
        await Assert.That(restored.ValueComparer == StringComparer.OrdinalIgnoreCase).IsTrue();
        await Assert.That(ReferenceEquals(actual[0], actual[1])).IsTrue();
        await Assert.That(((ImmutableDictionary<string, double>)actual[2])[MapValue]).IsEqualTo(4);
        await Assert.That(((ImmutableDictionary<string, long>)actual[3]).IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(NativeImmutableDictionaryFault.MissingValues)]
    [Arguments(NativeImmutableDictionaryFault.NullValues)]
    [Arguments(NativeImmutableDictionaryFault.DuplicateValues)]
    [Arguments(NativeImmutableDictionaryFault.IncompletePairs)]
    [Arguments(NativeImmutableDictionaryFault.OversizedCount)]
    [Arguments(NativeImmutableDictionaryFault.AlienMember)]
    [Arguments(NativeImmutableDictionaryFault.BaseScope)]
    [Arguments(NativeImmutableDictionaryFault.UnknownComparer)]
    [Arguments(NativeImmutableDictionaryFault.TypedNullComparer)]
    [Arguments(NativeImmutableDictionaryFault.DuplicateComparer)]
    [Arguments(NativeImmutableDictionaryFault.UnknownKeyComparer)]
    public async Task InvalidImmutableSurrogateCannotReachGeneratedMapAllocationAndHealthyMapStillReads(NativeImmutableDictionaryFault fault)
    {
        var bytes = NativeImmutableDictionaryFixture.Encode(fault);
        var originalBytes = bytes.ToArray();
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Validate(bytes));
        await NativeWireCollectionTests.Reject(() => NativeSerialization.Deserialize<ImmutableDictionary<string, string>>(bytes));
        await Assert.That(bytes.SequenceEqual(originalBytes)).IsTrue();
        var healthy = ImmutableDictionary<string, string>.Empty.Add(MapKey, MapValue);
        var actual = NativeSerialization.Deserialize<ImmutableDictionary<string, string>>(NativeSerialization.Serialize(healthy));
        await Assert.That(actual.Count).IsEqualTo(1);
        await Assert.That(actual[MapKey]).IsEqualTo(MapValue);
    }
}
