using System.Collections.Immutable;

namespace KeyLoad.UnitTests.InternalSerialization;

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
}
