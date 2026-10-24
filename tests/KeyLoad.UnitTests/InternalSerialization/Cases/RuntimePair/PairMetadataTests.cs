using System.Reflection;

namespace KeyLoad.UnitTests.InternalSerialization;

internal sealed class PairMetadataTests
{
    [Test]
    [Arguments(nameof(PairMetadataRecord.Direct))]
    [Arguments(nameof(PairMetadataRecord.Optional))]
    [Arguments(nameof(PairMetadataRecord.Items))]
    public async Task R13Ac002ActualMemberMetadataRetainsBothRequiredPairChildren(string memberName)
    {
        var context = new NullabilityInfoContext();
        var metadata = context.Create(typeof(PairMetadataRecord).GetProperty(memberName)!);
        if (memberName == nameof(PairMetadataRecord.Items))
        {
            metadata = metadata.GenericTypeArguments.Single();
        }
        var expectedType = memberName == nameof(PairMetadataRecord.Optional)
            ? typeof(KeyValuePair<string, string>?) : typeof(KeyValuePair<string, string>);
        await Assert.That(metadata.Type).IsEqualTo(expectedType);
        await Assert.That(metadata.GenericTypeArguments.Length).IsEqualTo(2);
        foreach (var child in metadata.GenericTypeArguments)
        {
            await Assert.That(child.Type).IsEqualTo(typeof(string));
            await Assert.That(child.ReadState).IsEqualTo(NullabilityState.NotNull);
        }
        var validation = ValueValidation.Create(metadata);
        await Assert.That(validation.Required).IsFalse();
        await Assert.That(validation.Element).IsNull();
        await Assert.That(validation.DictionaryKey!.Required).IsTrue();
        await Assert.That(validation.DictionaryValue!.Required).IsTrue();
    }

    [Test]
    [Arguments(PairMetadataShape.Direct, true)]
    [Arguments(PairMetadataShape.Direct, false)]
    [Arguments(PairMetadataShape.Nullable, true)]
    [Arguments(PairMetadataShape.Nullable, false)]
    [Arguments(PairMetadataShape.ImmutableArray, true)]
    [Arguments(PairMetadataShape.ImmutableArray, false)]
    public async Task R13Ac001RequiredNullKeysAndValuesRejectEveryTypedCodecEntry(PairMetadataShape shape, bool nullKey)
    {
        var value = PairMetadataFixtures.RequiredNull(shape, nullKey);
        await Reject(() => OrleansSerialization.Serialize(value));
        using var destination = new MemoryStream();
        await Reject(() => OrleansSerialization.Serialize(value, destination));
        await Assert.That(destination.Length).IsEqualTo(0);
        await Reject(() => OrleansSerialization.Measure(value));
        var bytes = NullableMetadataFixtures.EncodeUnchecked(value);
        await Reject(() => OrleansSerialization.Deserialize<PairMetadataRecord>(bytes));
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task R13Ac002ValidPresentAbsentAndEmptyPairCollectionsRoundtrip(bool present, bool empty)
    {
        var expected = PairMetadataFixtures.Create(present, empty);
        var bytes = OrleansSerialization.Serialize(expected);
        await Assert.That(OrleansSerialization.Measure(expected)).IsEqualTo((long)bytes.Length);
        var actual = OrleansSerialization.Deserialize<PairMetadataRecord>(bytes);
        await Assert.That(actual.Direct).IsEqualTo(expected.Direct);
        await Assert.That(actual.Optional).IsEqualTo(expected.Optional);
        await Assert.That(actual.Items.IsDefault).IsFalse();
        await Assert.That(actual.Items.IsEmpty).IsEqualTo(empty);
        await Assert.That(actual.Items.Length).IsEqualTo(expected.Items.Length);
        if (!empty)
        {
            await Assert.That(actual.Items[0]).IsEqualTo(expected.Items[0]);
        }
        await Assert.That(actual.NullableChildren).IsEqualTo(expected.NullableChildren);
        await Assert.That(actual.NullableItems.IsDefault).IsFalse();
        await Assert.That(actual.NullableItems.Length).IsEqualTo(1);
        await Assert.That(actual.NullableItems[0]).IsEqualTo(expected.NullableItems[0]);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task R13Ac002ExplicitNullableChildrenPreserveEachNullPolicy(bool nullKey, bool nullValue)
    {
        var pair = new KeyValuePair<string?, string?>(nullKey ? null : PairMetadataFixtures.Key,
            nullValue ? null : PairMetadataFixtures.Value);
        var expected = PairMetadataFixtures.Create(present: false, empty: true) with
        {
            NullableChildren = pair,
            NullableItems = [pair]
        };
        var bytes = OrleansSerialization.Serialize(expected);
        await Assert.That(OrleansSerialization.Measure(expected)).IsEqualTo((long)bytes.Length);
        var actual = OrleansSerialization.Deserialize<PairMetadataRecord>(bytes);
        await Assert.That(actual.NullableChildren).IsEqualTo(pair);
        await Assert.That(actual.NullableItems[0]).IsEqualTo(pair);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task R13Ac002MetadataFreeRootAndDynamicPairsRetainTheirNullableBehavior(bool nullKey, bool nullValue)
    {
        var expected = new KeyValuePair<string, string>(nullKey ? null! : PairMetadataFixtures.Key,
            nullValue ? null! : PairMetadataFixtures.Value);
        var bytes = OrleansSerialization.Serialize(expected);
        await Assert.That(OrleansSerialization.Measure(expected)).IsEqualTo((long)bytes.Length);
        await Assert.That(OrleansSerialization.Deserialize<KeyValuePair<string, string>>(bytes)).IsEqualTo(expected);
        var dynamicBytes = OrleansSerialization.Serialize<object>(expected);
        var dynamicPair = (KeyValuePair<string, string>)OrleansSerialization.Deserialize<object>(dynamicBytes);
        await Assert.That(dynamicPair).IsEqualTo(expected);
    }

    private static async Task Reject(Action action)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(action);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
    }
}
