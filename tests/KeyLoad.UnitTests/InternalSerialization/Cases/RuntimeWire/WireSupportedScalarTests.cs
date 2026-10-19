namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class WireSupportedScalarTests
{
    [Test]
    public async Task AcIsPerf006NullAndFrozenTerminalAllowlistRemainAccepted()
    {
        WireSupported.Require(null);
        foreach (var type in WireSupportedScalarFixtures.TerminalTypes)
        {
            WireSupported.Require(type);
        }
        foreach (var type in WireSupportedScalarFixtures.PrimitiveTypes)
        {
            await Assert.That(type.IsPrimitive).IsTrue();
        }
        WireSupported.Require(typeof(WireSupportedScalarGenericOwner<int>.State));
        WireSupported.Require(typeof(List<int>));
    }

    [Test]
    public async Task AcIsPerf006OpenAndUnsupportedShapesKeepTheSameCorruptionFailure()
    {
        var directVoidFailure = Assert.ThrowsExactly<KeyLoadException>(() => WireSupported.Require(typeof(void)));
        await Assert.That(directVoidFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(directVoidFailure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
        Type[] rejected =
        [
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<int>), typeof(int[,]), typeof(void),
            typeof(WireSupportedScalarGenericOwner<>.State),
            typeof(WireSupportedScalarGenericOwner<HashSet<int>>.State)
        ];
        foreach (var type in rejected)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => WireSupported.Require(type));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
        }
    }

    [Test]
    public async Task AcIsPerf006GeneratedScalarCollectionAndByteOwnershipValuesRemainReadable()
    {
        byte[] sourceBytes = [3, 5, 8, 13];
        var value = new WireSupportedScalarRecord(41, WireSupportedScalarKind.Ready,
            "execution-scalar-λ", sourceBytes, [2, 7, 11]);
        var bytes = OrleansSerialization.Serialize(value);
        var restored = OrleansSerialization.Deserialize<WireSupportedScalarRecord>(bytes);
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
        var nullableTwin = value with
        {
            Identifier = null
        };
        var nullableBytes = OrleansSerialization.Serialize(nullableTwin);
        var restoredNullableTwin = OrleansSerialization.Deserialize<WireSupportedScalarRecord>(nullableBytes);
        await Assert.That(restoredNullableTwin.Identifier).IsNull();
        await Assert.That(restoredNullableTwin.Kind).IsEqualTo(value.Kind);
        await Assert.That(restoredNullableTwin.Name).IsEqualTo(value.Name);
        await Assert.That(restoredNullableTwin.Payload).IsEquivalentTo(new byte[] { 3, 5, 8, 13 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(restoredNullableTwin.Values).IsEquivalentTo(new[] { 2, 7, 11 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
