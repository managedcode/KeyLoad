using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalLifecycleTests
{
    private const ulong CheckpointMagic = 0x32545043444C4BUL;
    private const int SigningKeyBytes = 32;
    private const long AppliedPosition = 7;
    private const string SnapshotFileName = "snapshot";
    private const string BackupDirectoryName = "backup";
    private const string RestoreDirectoryName = "restored";
    private const string SystemNamespace = "system";
    private const string LastAppliedKey = "last-applied";

    [Test]
    public async Task AcWal004CompactionKeepsCheckpointTwoAndIdentityThreeAfterBinaryWrites()
    {
        using var files = new WalFileFixture();
        StoreIdentity original;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            original = store.Identity;
            var snapshot = store.Compact();
            await Assert.That(snapshot.Position).IsEqualTo(1L);
            await Assert.That(snapshot.RecordCount).IsEqualTo(1L);
            await Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(await files.ReadJournalAsync()))
                .IsEqualTo(CheckpointMagic);
            await WalFileFixture.AssertPreservedIdentity(original, store.Identity);
            await WalFileFixture.AssertPreservedIdentity(original, await files.ReadIdentityAsync());
        }
        files.RemoveMaterializedTree();
        using var reopened = new ZoneTreeStore(new(files.DirectoryPath));
        await WalFileFixture.AssertPreservedIdentity(original, reopened.Identity);
        await Assert.That(reopened.Position).IsEqualTo(1L);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x10])))
            .IsEquivalentTo(new byte[] { 0x30 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcWal004InstallSnapshotKeepsCurrentIdentityAndExistingCheckpointInteroperability()
    {
        using var sourceFiles = new WalFileFixture();
        using var targetFiles = new WalFileFixture();
        var incarnation = Guid.NewGuid();
        var signingKey = RandomNumberGenerator.GetBytes(SigningKeyBytes);
        using var source = new ZoneTreeStore(new(sourceFiles.DirectoryPath) { Incarnation = incarnation, SigningKey = signingKey });
        source.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(SystemNamespace, LastAppliedKey), AppliedPosition);
            transaction.Put([0x10], []);
            return true;
        });
        var snapshot = Path.Combine(sourceFiles.DirectoryPath, SnapshotFileName);
        source.CreateSnapshot(snapshot, AppliedPosition);
        StoreIdentity installedIdentity;
        using (var target = new ZoneTreeStore(new(targetFiles.DirectoryPath) { Incarnation = incarnation, SigningKey = signingKey }))
        {
            target.Commit((transaction, _) => { transaction.Put([0x20], [0x40]); return true; });
            var original = target.Identity;
            var installed = target.InstallSnapshot(snapshot, AppliedPosition);
            await Assert.That(installed.AppliedPosition).IsEqualTo(AppliedPosition);
            installedIdentity = original with { ReadGeneration = original.ReadGeneration + 1 };
            await WalFileFixture.AssertPreservedIdentity(installedIdentity, target.Identity);
            await WalFileFixture.AssertPreservedIdentity(installedIdentity, await targetFiles.ReadIdentityAsync());
            await Assert.That(target.Read(view => view.ReadOwnedValue([0x20]))).IsNull();
        }
        using var reopened = new ZoneTreeStore(new(targetFiles.DirectoryPath));
        await WalFileFixture.AssertPreservedIdentity(installedIdentity, reopened.Identity);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x10]))).IsNotNull();
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x10]))).IsEmpty();
    }

    [Test]
    public async Task AcWal004BackupRestoreReturnsPersistedCurrentIdentityAndReopensLogicalBytes()
    {
        using var files = new WalFileFixture();
        var backup = Path.Combine(files.DirectoryPath, BackupDirectoryName);
        var destination = Path.Combine(files.DirectoryPath, RestoreDirectoryName);
        StoreIdentity original;
        using (var source = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            source.Commit((transaction, _) => { transaction.Put([0x10, 0xFF], [0x00, 0x80]); return true; });
            original = source.Identity;
            await Assert.That(source.CreateBackup(backup)).IsEqualTo(1L);
        }
        var restored = ZoneTreeStore.Restore(backup, destination);
        await Assert.That(restored.FormatVersion).IsEqualTo(WalFileFixture.CurrentIdentityVersion);
        await Assert.That(restored.NodeId == original.NodeId).IsFalse();
        await Assert.That(restored.DispatchPaused).IsTrue();
        using var reopened = new ZoneTreeStore(new(destination));
        await WalFileFixture.AssertPreservedIdentity(restored, reopened.Identity);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x10, 0xFF])))
            .IsEquivalentTo(new byte[] { 0x00, 0x80 }, CollectionOrdering.Matching);
    }
}
