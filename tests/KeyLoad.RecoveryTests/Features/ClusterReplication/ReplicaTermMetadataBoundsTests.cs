using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataBoundsTests
{
    private const string SnapshotHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string GuidFormat = "N";
    private const int SnapshotLength = 1;
    private const long SnapshotIndex = 1;
    private const long SnapshotTerm = 7;

    [Test]
    public async Task ZeroInvalidAndSnapshotTermsKeepExistingBoundaries()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        await Assert.That(fixture.Log.TermAt(0)).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(-1)).Code)
            .IsEqualTo(ErrorCode.NotFound);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(1)).Code)
            .IsEqualTo(ErrorCode.NotFound);

        var snapshot = Snapshot(fixture.Configuration.Incarnation, SnapshotIndex, SnapshotTerm);
        fixture.Log.PublishSnapshot(snapshot);
        await Assert.That(fixture.Log.TermAt(SnapshotIndex)).IsEqualTo(SnapshotTerm);
        await Assert.That(fixture.Log.TermAt(0)).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(2)).Code)
            .IsEqualTo(ErrorCode.NotFound);
        await Assert.That(fixture.Store.GetReadDiagnostics().BorrowedPointLookups).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidSnapshotTermIsRejectedWithoutChangingTheCut()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        var invalid = Snapshot(fixture.Configuration.Incarnation, SnapshotIndex, 0);

        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.PublishSnapshot(invalid)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Log.State.Snapshot).IsNull();
        await Assert.That(fixture.Log.TermAt(0)).IsEqualTo(0);
    }

    private static ReplicaSnapshot Snapshot(Guid incarnation, long index, long term)
    {
        var transferId = Guid.NewGuid();
        return new(transferId, incarnation, index, term, SnapshotLength, SnapshotHash,
            transferId.ToString(GuidFormat) + ReplicaProtocol.SnapshotExtension);
    }
}
