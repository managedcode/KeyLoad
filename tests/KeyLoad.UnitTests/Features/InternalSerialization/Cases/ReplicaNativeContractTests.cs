using System.Collections.Immutable;
using System.Text;
using KeyLoad.Replication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-001/005: native replica values retain exact command content and reject incompatible encodings.</summary>
internal sealed class ReplicaNativeContractTests
{
    private const string Voter = "voter-a";
    private const string Principal = "principal";
    private const string Payload = " {\n \"text\" : \"Слава Україні 🙂\\\"\\\\\", \"n\" : 1.00 } ";
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string GuidFormat = "N";
    private const byte TrailingByte = 0x7F;

    [Test]
    public async Task NativeOperationAndSharedAppendPayloadPreserveExactContent()
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch, Principal,
            DateTimeOffset.UnixEpoch.AddMinutes(1), Payload));
        var entries = ImmutableArray.Create(new ReplicaEntry(1, 2, operation), new ReplicaEntry(2, 2, operation with { }));
        var append = new AppendRequest(Voter, 2, 0, 0, 1, entries);
        var encoded = ReplicaProtocolCodec.Serialize(append);
        var decoded = ReplicaProtocolCodec.Deserialize<AppendRequest>(encoded);
        await Assert.That(decoded.LeaderId).IsEqualTo(Voter);
        await Assert.That(decoded.Entries.Length).IsEqualTo(entries.Length);
        await Assert.That(decoded.Entries[0].Index).IsEqualTo(entries[0].Index);
        await Assert.That(decoded.Entries[1].Index).IsEqualTo(entries[1].Index);
        await Assert.That(decoded.Entries[0].Term).IsEqualTo(entries[0].Term);
        await Assert.That(decoded.Entries[1].Term).IsEqualTo(entries[1].Term);
        await AssertOperationAsync(operation, decoded.Entries[0].Operation!);
        await AssertOperationAsync(operation, decoded.Entries[1].Operation!);
        await Assert.That(decoded.Entries[0].Operation!.PayloadJson).IsEqualTo(Payload);
        await Assert.That(Encoding.UTF8.GetBytes(decoded.Entries[1].Operation!.PayloadJson))
            .IsEquivalentTo(Encoding.UTF8.GetBytes(Payload), CollectionOrdering.Matching);
        await Assert.That(ReplicaProtocolCodec.Measure(append)).IsEqualTo(encoded.LongLength);
        await Assert.That(ReplicaProtocolCodec.MeasureEntries(entries))
            .IsEqualTo(ReplicaProtocolCodec.SerializeEntries(entries).LongLength);
        await AssertOperationAsync(operation, ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(operation)));
    }

    [Test]
    public async Task NativeControlRequestsAndRepliesRetainInitializedEmptyHeartbeat()
    {
        await RoundTripAsync(new VoteRequest(Voter, 2, 3, 1));
        await RoundTripAsync(new VoteReply(2, true));
        await RoundTripAsync(new AppendReply(2, true, 3, 4));
        await RoundTripAsync(new ReadBarrierReceipt(Guid.NewGuid(), 3, 2));
        await RoundTripAsync(string.Empty);
        var heartbeat = new AppendRequest(Voter, 2, 3, 1, 3, []);
        var decoded = ReplicaProtocolCodec.Deserialize<AppendRequest>(ReplicaProtocolCodec.Serialize(heartbeat));
        await Assert.That(decoded.Entries.IsDefault).IsFalse();
        await Assert.That(decoded.Entries.Length).IsEqualTo(0);
        await Assert.That(decoded.PreviousIndex).IsEqualTo(3);
        await Assert.That(decoded.CommittedIndex).IsEqualTo(3);
    }

    [Test]
    public async Task NativeSnapshotsRetainDescriptorsAndRawChunkBytes()
    {
        var transfer = Guid.NewGuid();
        var snapshot = new ReplicaSnapshot(transfer, Guid.NewGuid(), 3, 2, 4, Hash,
            transfer.ToString(GuidFormat) + ReplicaProtocol.SnapshotExtension);
        await RoundTripAsync(snapshot);
        await RoundTripAsync(new SnapshotBeginRequest(Voter, 2, snapshot));
        await RoundTripAsync(new SnapshotCompleteRequest(Voter, 2, transfer));
        await RoundTripAsync(new SnapshotReply(2, 4, true, 3));
        byte[] bytes = [0x00, 0xFF, 0x80, 0x41];
        var chunk = new SnapshotChunkRequest(Voter, 2, transfer, 0, bytes);
        var encoded = ReplicaProtocolCodec.Serialize(chunk);
        var decoded = ReplicaProtocolCodec.Deserialize<SnapshotChunkRequest>(encoded);
        await Assert.That(decoded.TransferId).IsEqualTo(transfer);
        await Assert.That(decoded.Bytes.ToArray()).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await Assert.That(ReplicaProtocolCodec.Measure(chunk)).IsEqualTo(encoded.LongLength);
    }

    [Test]
    public async Task UnknownReplicaPrefixFailsExplicitly()
    {
        var request = new VoteRequest(Voter, 2, 0, 0);
        var unknown = ReplicaProtocolCodec.Serialize(request);
        unknown[0] ^= TrailingByte;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<VoteRequest>(unknown)).Code)
            .IsEqualTo(ErrorCode.FormatUnsupported);
    }

    [Test]
    public async Task CurrentReplicaPrefixDoesNotPermitMissingBodyOrTrailingBytes()
    {
        var encoded = ReplicaProtocolCodec.Serialize(new VoteRequest(Voter, 2, 0, 0));
        var missing = encoded[..ReplicaProtocol.PayloadPrefixBytes];
        var trailing = encoded.Append(TrailingByte).ToArray();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<VoteRequest>(missing)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<VoteRequest>(trailing)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    private static async Task RoundTripAsync<T>(T value)
    {
        var encoded = ReplicaProtocolCodec.Serialize(value);
        await Assert.That(ReplicaProtocolCodec.Deserialize<T>(encoded)).IsEqualTo(value);
        await Assert.That(ReplicaProtocolCodec.Measure(value)).IsEqualTo(encoded.LongLength);
    }

    private static async Task AssertOperationAsync(ReplicatedOperation expected, ReplicatedOperation actual)
    {
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Kind).IsEqualTo(expected.Kind);
        await Assert.That(actual.PrincipalId).IsEqualTo(expected.PrincipalId);
        await Assert.That(actual.EvaluatedAt).IsEqualTo(expected.EvaluatedAt);
        await Assert.That(actual.PayloadJson).IsEqualTo(expected.PayloadJson);
        await Assert.That(actual.NativePayload.Span.SequenceEqual(expected.NativePayload.Span)).IsTrue();
    }
}
