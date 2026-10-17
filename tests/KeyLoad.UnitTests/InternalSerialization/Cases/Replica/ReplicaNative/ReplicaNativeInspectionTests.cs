using System.Collections.Immutable;
using System.Text;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-005: genuine native codecs validate admission shape without decoding opaque content.</summary>
internal sealed class ReplicaNativeInspectionTests
{
    private const string Voter = "voter-a";
    private const string Principal = "principal-Україна-🙂";
    private const string BooleanPayload = " true ";
    private const int MaximumEntries = 2;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeSharedStringAndOperationReferencesRetainExactBatchMeasure(bool sharedOperation)
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, BooleanPayload));
        var copy = sharedOperation ? operation : operation with
        {
        };
        ImmutableArray<ReplicaEntry> entries = [new(1, 1, operation), new(2, 1, copy)];
        var request = new AppendRequest(Voter, 1, 0, 0, 0, entries);
        var inspected = ReplicaNativeInspection.Inspect<AppendRequest>(ReplicaProtocolCodec.Serialize(request), MaximumEntries);
        var first = inspected.Value.Entries[0].Operation!;
        var second = inspected.Value.Entries[1].Operation!;
        await Assert.That(inspected.Metadata(inspected.Value.LeaderId, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(inspected.Metadata(first.PrincipalId, Encoding.UTF8.GetByteCount(Principal))).IsEqualTo(Principal);
        await Assert.That(inspected.Utf8Length(first.PayloadJson)).IsEqualTo(Encoding.UTF8.GetByteCount(BooleanPayload));
        await Assert.That(ReferenceEquals(first.PayloadJson, second.PayloadJson)).IsTrue();
        await Assert.That(first.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        await Assert.That(inspected.MeasureEntries(inspected.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.MeasureEntries(entries));
        await Assert.That(inspected.MeasureEntries(inspected.Value.Entries))
            .IsEqualTo(ReplicaProtocolCodec.SerializeEntries(entries).LongLength);
    }

    [Test]
    public async Task NativeHeartbeatAndReadBarrierAreInitializedAndEmpty()
    {
        var heartbeat = new AppendRequest(Voter, 1, 0, 0, 0, []);
        var inspected = ReplicaNativeInspection.Inspect<AppendRequest>(ReplicaProtocolCodec.Serialize(heartbeat), MaximumEntries);
        await Assert.That(inspected.Value.Entries.IsDefault).IsFalse();
        await Assert.That(inspected.Value.Entries.Length).IsEqualTo(0);
        var read = ReplicaNativeInspection.Inspect<string>(ReplicaProtocolCodec.Serialize(string.Empty), MaximumEntries);
        await Assert.That(read.Utf8Length(read.Value)).IsEqualTo(0);
    }

    [Test]
    [Arguments(ReplicaMalformedVoteShape.Duplicate)]
    [Arguments(ReplicaMalformedVoteShape.Missing)]
    [Arguments(ReplicaMalformedVoteShape.Unknown)]
    [Arguments(ReplicaMalformedVoteShape.WrongType)]
    public async Task NativeMalformedVoteShapeIsRejected(ReplicaMalformedVoteShape shape)
    {
        var bytes = ReplicaNativeFixtureWriter.Vote(shape, new(Voter, 1, 0, 0));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<VoteRequest>(bytes, MaximumEntries));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(ReplicaMalformedArrayShape.Underfilled)]
    [Arguments(ReplicaMalformedArrayShape.Overfilled)]
    [Arguments(ReplicaMalformedArrayShape.Null)]
    public async Task NativeDeclaredEntryArrayMustMatchActualElements(ReplicaMalformedArrayShape shape)
    {
        var bytes = ReplicaNativeFixtureWriter.Batch(shape, new([new(1, 1, null), new(2, 1, null)]));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicaEntryBatch>(bytes, MaximumEntries));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task NativeWrongDynamicRootAndEntryCountAboveExistingLimitFailBeforeFullDecode()
    {
        var wrong = ReplicaProtocolCodec.Serialize(new AppendReply(1, true, 1, 2));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<VoteRequest>(wrong, MaximumEntries)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var entries = new ReplicaEntryBatch([new(1, 1, null), new(2, 1, null)]);
        var encoded = ReplicaProtocolCodec.Serialize(entries);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicaEntryBatch>(encoded, 1)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }
}
