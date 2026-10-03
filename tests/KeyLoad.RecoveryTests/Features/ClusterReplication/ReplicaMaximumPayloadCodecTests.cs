using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaMaximumPayloadCodecTests
{
    [Test]
    public async Task R12Ac002SameOperationWriterWithoutDefectPreservesEveryFieldAndNativeBytes()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            var expected = ReplicaMaximumPayloadControlFixtures.Operation(fixture);
            var bytes = ReplicaMaximumPayload.Malformed(expected, ReplicaMaximumPayload.ValidFields);
            var actual = ReplicaProtocolCodec.DeserializeStored<ReplicatedOperation>(bytes, fixture.Configuration.MaxAppendEntries);
            await ReplicaMaximumPayloadControlFixtures.AssertEquivalentAsync(actual, expected);
        });
    }

    [Test]
    public async Task R12Ac002SameNestedEntryWriterWithoutDefectPreservesNativeOperationBytes()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            var expected = new ReplicaEntry(1, 2, ReplicaMaximumPayloadControlFixtures.Operation(fixture));
            var bytes = ReplicaMaximumPayloadControlFixtures.Nested(expected, ReplicaMaximumPayload.ValidFields);
            var actual = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(bytes, fixture.Configuration.MaxAppendEntries);
            await Assert.That(actual.Index).IsEqualTo(expected.Index);
            await Assert.That(actual.Term).IsEqualTo(expected.Term);
            await Assert.That(actual.Operation).IsNotNull();
            await ReplicaMaximumPayloadControlFixtures.AssertEquivalentAsync(actual.Operation!, expected.Operation!);
        });
    }

    [Test]
    [Arguments(ReplicaMaximumPayload.UnknownField)]
    [Arguments(ReplicaMaximumPayload.DuplicateField)]
    public async Task R12Ac002RequestedFieldDefectsRejectBothCurrentOperationAndNestedEntry(string shape)
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            var operation = ReplicaMaximumPayloadControlFixtures.Operation(fixture);
            var operationBytes = ReplicaMaximumPayload.Malformed(operation, shape);
            var entryBytes = ReplicaMaximumPayloadControlFixtures.Nested(new(1, 2, operation), shape);
            var operationFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
                ReplicaProtocolCodec.DeserializeStored<ReplicatedOperation>(operationBytes, fixture.Configuration.MaxAppendEntries));
            var entryFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
                ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(entryBytes, fixture.Configuration.MaxAppendEntries));
            await Assert.That(operationFailure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(entryFailure.Code).IsEqualTo(ErrorCode.Corruption);
        });
    }
}
