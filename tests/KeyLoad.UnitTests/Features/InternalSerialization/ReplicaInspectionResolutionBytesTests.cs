using System.Runtime.InteropServices;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaInspectionResolutionBytesTests
{
    [Test]
    public async Task AcIs005GeneratedSnapshotChunkUsesBorrowedSpecializedBytesAndExactFields()
    {
        byte[] data = [0, 0x81, 0xfe, 0xff];
        var expected = new SnapshotChunkRequest("leader", 3, Guid.NewGuid(), 7, data);
        var bytes = ReplicaProtocolCodec.Serialize(expected);
        var inspected = ReplicaNativeInspection.Inspect<SnapshotChunkRequest>(bytes, maximumEntries: 2);
        await Assert.That(inspected.Value.Bytes.Span.SequenceEqual(data)).IsTrue();
        await Assert.That(inspected.Value.Term).IsEqualTo(expected.Term);
        await Assert.That(inspected.Value.Offset).IsEqualTo(expected.Offset);
        await Assert.That(inspected.Value.TransferId).IsEqualTo(expected.TransferId);
        await Assert.That(inspected.Metadata(inspected.Value.LeaderId, 32)).IsEqualTo(expected.LeaderId);
        await Assert.That(MemoryMarshal.TryGetArray(inspected.Value.Bytes, out var segment)).IsTrue();
        await Assert.That(ReferenceEquals(segment.Array, bytes)).IsTrue();
        await Assert.That(segment.Offset >= ReplicaProtocol.PayloadPrefixBytes).IsTrue();
        var owned = ReplicaProtocolCodec.Deserialize<SnapshotChunkRequest>(bytes);
        bytes.AsSpan(segment.Offset, segment.Count).Clear();
        await Assert.That(inspected.Value.Bytes.Span.SequenceEqual(new byte[data.Length])).IsTrue();
        await Assert.That(owned.Bytes.Span.SequenceEqual(data)).IsTrue();
    }

    [Test]
    public async Task AcIs005GeneratedHardStatePreservesOptionalSnapshotWithSamePrivateProfile()
    {
        var expected = new ReplicaHardState(ReplicaProtocol.FormatVersion, Guid.NewGuid(), 3, "voter", 7, 5, null);
        var bytes = ReplicaProtocolCodec.Serialize(expected);
        var inspected = ReplicaNativeInspection.Inspect<ReplicaHardState>(bytes, maximumEntries: 2);
        await Assert.That(inspected.Value.Version).IsEqualTo(expected.Version);
        await Assert.That(inspected.Value.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(inspected.Value.Term).IsEqualTo(expected.Term);
        await Assert.That(inspected.Value.LastIndex).IsEqualTo(expected.LastIndex);
        await Assert.That(inspected.Value.CommittedIndex).IsEqualTo(expected.CommittedIndex);
        await Assert.That(inspected.Value.Snapshot).IsNull();
        await Assert.That(inspected.Metadata(inspected.Value.VotedFor!, 32)).IsEqualTo(expected.VotedFor);
    }
}
