using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeWireSupportedScalarTests
{
    [Test]
    public async Task AcIsPerf006NullAndFrozenTerminalAllowlistRemainAccepted()
    {
        NativeWireSupported.Require(null);
        foreach (var type in NativeWireSupportedScalarFixtures.TerminalTypes)
        {
            NativeWireSupported.Require(type);
        }
        foreach (var type in NativeWireSupportedScalarFixtures.PrimitiveTypes)
        {
            await Assert.That(type.IsPrimitive).IsTrue();
        }
        NativeWireSupported.Require(typeof(NativeWireSupportedScalarGenericOwner<int>.State));
        NativeWireSupported.Require(typeof(List<int>));
    }

    [Test]
    public async Task AcIsPerf006OpenAndUnsupportedShapesKeepTheSameCorruptionFailure()
    {
        var directVoidFailure = Assert.ThrowsExactly<KeyLoadException>(() => NativeWireSupported.Require(typeof(void)));
        await Assert.That(directVoidFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(directVoidFailure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
        Type[] rejected =
        [
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<int>), typeof(int[,]), typeof(void),
            typeof(NativeWireSupportedScalarGenericOwner<>.State),
            typeof(NativeWireSupportedScalarGenericOwner<HashSet<int>>.State)
        ];
        foreach (var type in rejected)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeWireSupported.Require(type));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
        }
    }

    [Test]
    public async Task AcIsPerf006GeneratedScalarCollectionAndByteOwnershipValuesRemainReadable()
    {
        byte[] sourceBytes = [3, 5, 8, 13];
        var value = new NativeWireSupportedScalarRecord(41, NativeWireSupportedScalarKind.Ready,
            "native-scalar-λ", sourceBytes, [2, 7, 11]);
        var bytes = NativeSerialization.Serialize(value);
        var restored = NativeSerialization.Deserialize<NativeWireSupportedScalarRecord>(bytes);
        await Assert.That(restored.Identifier).IsEqualTo(value.Identifier);
        await Assert.That(restored.Kind).IsEqualTo(value.Kind);
        await Assert.That(restored.Name).IsEqualTo(value.Name);
        await Assert.That(restored.Payload).IsEquivalentTo(new byte[] { 3, 5, 8, 13 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(restored.Values).IsEquivalentTo(new[] { 2, 7, 11 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(ReferenceEquals(sourceBytes, restored.Payload)).IsFalse();
        sourceBytes[0] = 99;
        await Assert.That(restored.Payload[0]).IsEqualTo((byte)3);
        restored.Payload[1] = 100;
        await Assert.That(sourceBytes[1]).IsEqualTo((byte)5);

        sourceBytes[0] = 3;
        var nullableTwin = value with { Identifier = null };
        var nullableBytes = NativeSerialization.Serialize(nullableTwin);
        var restoredNullableTwin = NativeSerialization.Deserialize<NativeWireSupportedScalarRecord>(nullableBytes);
        await Assert.That(restoredNullableTwin.Identifier).IsNull();
        await Assert.That(restoredNullableTwin.Kind).IsEqualTo(value.Kind);
        await Assert.That(restoredNullableTwin.Name).IsEqualTo(value.Name);
        await Assert.That(restoredNullableTwin.Payload).IsEquivalentTo(new byte[] { 3, 5, 8, 13 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(restoredNullableTwin.Values).IsEquivalentTo(new[] { 2, 7, 11 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
