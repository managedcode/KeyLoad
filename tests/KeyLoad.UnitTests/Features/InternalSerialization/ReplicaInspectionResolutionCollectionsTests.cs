using System.Collections.Immutable;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaInspectionResolutionCollectionsTests
{
    [Test]
    public async Task AcIs005GeneratedClosedEntryArraysHonorTheConfiguredLimitInBatchAndAppend()
    {
        ImmutableArray<ReplicaEntry> entries = [new(1, 3, null), new(2, 3, null)];
        var batch = ReplicaProtocolCodec.Serialize(new ReplicaEntryBatch(entries));
        var append = ReplicaProtocolCodec.Serialize(new AppendRequest("leader", 3, 0, 0, 0, entries));
        var batchError = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicaEntryBatch>(batch, 1));
        var appendError = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<AppendRequest>(append, 1));
        await Assert.That(batchError.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(appendError.Code).IsEqualTo(ErrorCode.Corruption);
        var accepted = ReplicaNativeInspection.Inspect<AppendRequest>(append, 2);
        await Assert.That(accepted.Value.Entries.Length).IsEqualTo(2);
        await Assert.That(accepted.Value.Entries[1].Index).IsEqualTo(2L);
        await Assert.That(accepted.MeasureEntries(accepted.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.MeasureEntries(entries));
    }

    [Test]
    public async Task AcIs005GeneratedVoterArraysUseBorrowedStringReferencesAndExistingCountBound()
    {
        var voter = new string("voter-Україна".ToCharArray());
        var record = new ReplicaBenchmarkMembershipRecord(ReplicaProtocol.FormatVersion, Guid.NewGuid(), [voter, voter]);
        var bytes = ReplicaProtocolCodec.Serialize(record);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicaBenchmarkMembershipRecord>(bytes, 1));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
        var inspected = ReplicaNativeInspection.Inspect<ReplicaBenchmarkMembershipRecord>(bytes, 2);
        await Assert.That(inspected.Value.VoterIds.Length).IsEqualTo(2);
        await Assert.That(ReferenceEquals(inspected.Value.VoterIds[0], inspected.Value.VoterIds[1])).IsTrue();
        await Assert.That(inspected.Metadata(inspected.Value.VoterIds[0], 64)).IsEqualTo(voter);
        await Assert.That(inspected.Metadata(inspected.Value.VoterIds[1], 64)).IsEqualTo(voter);
        var empty = ReplicaProtocolCodec.Serialize(record with { VoterIds = [] });
        var heartbeat = ReplicaNativeInspection.Inspect<ReplicaBenchmarkMembershipRecord>(empty, 0);
        await Assert.That(heartbeat.Value.VoterIds.IsDefault).IsFalse();
        await Assert.That(heartbeat.Value.VoterIds.IsEmpty).IsTrue();
    }
}
