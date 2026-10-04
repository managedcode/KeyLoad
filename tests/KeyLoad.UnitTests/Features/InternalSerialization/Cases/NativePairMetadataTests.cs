using System.Reflection;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativePairMetadataTests
{
    [Test]
    [Arguments(nameof(NativePairMetadataRecord.Direct))]
    [Arguments(nameof(NativePairMetadataRecord.Optional))]
    [Arguments(nameof(NativePairMetadataRecord.Items))]
    public async Task R13Ac002ActualMemberMetadataRetainsBothRequiredPairChildren(string memberName)
    {
        var context = new NullabilityInfoContext();
        var metadata = context.Create(typeof(NativePairMetadataRecord).GetProperty(memberName)!);
        if (memberName == nameof(NativePairMetadataRecord.Items))
        {
            metadata = metadata.GenericTypeArguments.Single();
        }
        var expectedType = memberName == nameof(NativePairMetadataRecord.Optional)
            ? typeof(KeyValuePair<string, string>?) : typeof(KeyValuePair<string, string>);
        await Assert.That(metadata.Type).IsEqualTo(expectedType);
        await Assert.That(metadata.GenericTypeArguments.Length).IsEqualTo(2);
        foreach (var child in metadata.GenericTypeArguments)
        {
            await Assert.That(child.Type).IsEqualTo(typeof(string));
            await Assert.That(child.ReadState).IsEqualTo(NullabilityState.NotNull);
        }
        var validation = NativeValueValidation.Create(metadata);
        await Assert.That(validation.Required).IsFalse();
        await Assert.That(validation.Element).IsNull();
        await Assert.That(validation.DictionaryKey!.Required).IsTrue();
        await Assert.That(validation.DictionaryValue!.Required).IsTrue();
    }

    [Test]
    [Arguments(NativePairMetadataShape.Direct, true)]
    [Arguments(NativePairMetadataShape.Direct, false)]
    [Arguments(NativePairMetadataShape.Nullable, true)]
    [Arguments(NativePairMetadataShape.Nullable, false)]
    [Arguments(NativePairMetadataShape.ImmutableArray, true)]
    [Arguments(NativePairMetadataShape.ImmutableArray, false)]
    public async Task R13Ac001RequiredNullKeysAndValuesRejectEveryTypedCodecEntry(NativePairMetadataShape shape, bool nullKey)
    {
        var value = NativePairMetadataFixtures.RequiredNull(shape, nullKey);
        await Reject(() => NativeSerialization.Serialize(value));
        using var destination = new MemoryStream();
        await Reject(() => NativeSerialization.Serialize(value, destination));
        await Assert.That(destination.Length).IsEqualTo(0);
        await Reject(() => NativeSerialization.Measure(value));
        var bytes = NativeNullableMetadataFixtures.EncodeUnchecked(value);
        await Reject(() => NativeSerialization.Deserialize<NativePairMetadataRecord>(bytes));
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task R13Ac002ValidPresentAbsentAndEmptyPairCollectionsRoundtrip(bool present, bool empty)
    {
        var expected = NativePairMetadataFixtures.Create(present, empty);
        var bytes = NativeSerialization.Serialize(expected);
        await Assert.That(NativeSerialization.Measure(expected)).IsEqualTo((long)bytes.Length);
        var actual = NativeSerialization.Deserialize<NativePairMetadataRecord>(bytes);
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
        var pair = new KeyValuePair<string?, string?>(nullKey ? null : NativePairMetadataFixtures.Key,
            nullValue ? null : NativePairMetadataFixtures.Value);
        var expected = NativePairMetadataFixtures.Create(present: false, empty: true) with
        {
            NullableChildren = pair,
            NullableItems = [pair]
        };
        var bytes = NativeSerialization.Serialize(expected);
        await Assert.That(NativeSerialization.Measure(expected)).IsEqualTo((long)bytes.Length);
        var actual = NativeSerialization.Deserialize<NativePairMetadataRecord>(bytes);
        await Assert.That(actual.NullableChildren).IsEqualTo(pair);
        await Assert.That(actual.NullableItems[0]).IsEqualTo(pair);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task R13Ac002MetadataFreeRootAndDynamicPairsRetainTheirNullableBehavior(bool nullKey, bool nullValue)
    {
        var expected = new KeyValuePair<string, string>(nullKey ? null! : NativePairMetadataFixtures.Key,
            nullValue ? null! : NativePairMetadataFixtures.Value);
        var bytes = NativeSerialization.Serialize(expected);
        await Assert.That(NativeSerialization.Measure(expected)).IsEqualTo((long)bytes.Length);
        await Assert.That(NativeSerialization.Deserialize<KeyValuePair<string, string>>(bytes)).IsEqualTo(expected);
        var dynamicBytes = NativeSerialization.Serialize<object>(expected);
        var dynamicPair = (KeyValuePair<string, string>)NativeSerialization.Deserialize<object>(dynamicBytes);
        await Assert.That(dynamicPair).IsEqualTo(expected);
    }

    private static async Task Reject(Action action)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(action);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
    }
}
