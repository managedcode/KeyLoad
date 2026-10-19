using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ImmutableDictionaryTests
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
        var bytes = OrleansSerialization.Serialize(original);
        OrleansSerialization.Validate(bytes);
        var actual = OrleansSerialization.Deserialize<object[]>(bytes);
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
    [Arguments(ImmutableDictionaryFault.MissingValues)]
    [Arguments(ImmutableDictionaryFault.NullValues)]
    [Arguments(ImmutableDictionaryFault.DuplicateValues)]
    [Arguments(ImmutableDictionaryFault.IncompletePairs)]
    [Arguments(ImmutableDictionaryFault.OversizedCount)]
    [Arguments(ImmutableDictionaryFault.AlienMember)]
    [Arguments(ImmutableDictionaryFault.BaseScope)]
    [Arguments(ImmutableDictionaryFault.UnknownComparer)]
    [Arguments(ImmutableDictionaryFault.TypedNullComparer)]
    [Arguments(ImmutableDictionaryFault.DuplicateComparer)]
    [Arguments(ImmutableDictionaryFault.UnknownKeyComparer)]
    public async Task InvalidImmutableSurrogateCannotReachGeneratedMapAllocationAndHealthyMapStillReads(ImmutableDictionaryFault fault)
    {
        var bytes = ImmutableDictionaryFixture.Encode(fault);
        var originalBytes = bytes.ToArray();
        await WireCollectionTests.Reject(() => OrleansSerialization.Validate(bytes));
        await WireCollectionTests.Reject(() => OrleansSerialization.Deserialize<ImmutableDictionary<string, string>>(bytes));
        await Assert.That(bytes.SequenceEqual(originalBytes)).IsTrue();
        var healthy = ImmutableDictionary<string, string>.Empty.Add(MapKey, MapValue);
        var actual = OrleansSerialization.Deserialize<ImmutableDictionary<string, string>>(OrleansSerialization.Serialize(healthy));
        await Assert.That(actual.Count).IsEqualTo(1);
        await Assert.That(actual[MapKey]).IsEqualTo(MapValue);
    }
}
