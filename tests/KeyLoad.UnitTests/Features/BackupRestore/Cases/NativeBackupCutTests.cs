using System.Buffers.Binary;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class NativeBackupCutTests
{
    private const ulong UnsupportedJournalSignature = ulong.MaxValue;

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs004HashConsistentTruncatedHeaderPreservesBackupAndDestination(bool existingEmpty)
    {
        using var fixture = new NativeBackupCutFixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        await fixture.RewriteJournalAsync(bytes[..(NativeBackupCutFixture.LastFrameOffset(bytes) + 1)]);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption, existingEmpty);
    }

    [Test]
    public async Task AcIs004HashConsistentTruncatedPayloadPreservesBackupAndDestination()
    {
        using var fixture = new NativeBackupCutFixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        await fixture.RewriteJournalAsync(bytes[..^1]);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test, Arguments(-1L), Arguments(1L)]
    public async Task AcIs004CompleteJournalRequiresExactManifestCut(long difference)
    {
        using var fixture = new NativeBackupCutFixture();
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.Backup);
        await MetadataTestFiles.WriteManifestAsync(fixture.Backup, manifest with { Position = manifest.Position + difference });
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs004MissingCommittedFrameCannotRestoreEvenWithMatchingFileHash(bool checkpoint)
    {
        using var fixture = new NativeBackupCutFixture(checkpoint);
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        await fixture.RewriteJournalAsync(bytes[..NativeBackupCutFixture.LastFrameOffset(bytes)]);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs004HashConsistentJournalStillRequiresFrameChecksum()
    {
        using var fixture = new NativeBackupCutFixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        bytes[^1] ^= 1;
        await fixture.RewriteJournalAsync(bytes);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs004HashConsistentJournalStillRequiresOrderedSequence()
    {
        using var fixture = new NativeBackupCutFixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(NativeBackupCutFixture.LastFrameOffset(bytes)
            + ZoneTreePersistenceFormat.SequenceOffset), fixture.Position + 1);
        await fixture.RewriteJournalAsync(bytes);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs004HashConsistentJournalStillRequiresNativeMutationPayload()
    {
        using var fixture = new NativeBackupCutFixture();
        await fixture.RewriteJournalAsync(WalFileFixture.CreateFrame([0]));
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.Backup);
        await MetadataTestFiles.WriteManifestAsync(fixture.Backup, manifest with { Position = 1 });
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs004HashConsistentUnsupportedJournalSignatureIsRefusedWithoutPublication()
    {
        using var fixture = new NativeBackupCutFixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, UnsupportedJournalSignature);
        await fixture.RewriteJournalAsync(bytes);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.FormatUnsupported);
    }

    [Test]
    public async Task AcIs004TruncatedCheckpointIsRefusedWithoutPublication()
    {
        using var fixture = new NativeBackupCutFixture(checkpoint: true);
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        await fixture.RewriteJournalAsync(bytes[..ZoneTreePersistenceFormat.HeaderLength]);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs004HashConsistentCheckpointStillRequiresCompleteFooter()
    {
        using var fixture = new NativeBackupCutFixture(checkpoint: true);
        var bytes = await File.ReadAllBytesAsync(fixture.Journal);
        var offset = 0;
        while (BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset)) != ZoneTreePersistenceFormat.CheckpointEndMagic)
        {
            offset += ZoneTreePersistenceFormat.HeaderLength + BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + ZoneTreePersistenceFormat.PayloadLengthOffset));
        }
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + ZoneTreePersistenceFormat.PayloadLengthOffset));
        var footer = NativeSerialization.Deserialize<ZoneTreeCheckpointFooter>(bytes.AsSpan(
            offset + ZoneTreePersistenceFormat.HeaderLength, length));
        var invalidFooter = WalFileFixture.CreateFrame(NativeSerialization.Serialize(footer with { Records = footer.Records + 1 }),
            sequence: 1, magic: ZoneTreePersistenceFormat.CheckpointEndMagic);
        await fixture.RewriteJournalAsync([.. bytes[..offset], .. invalidFooter,
            .. bytes[(offset + ZoneTreePersistenceFormat.HeaderLength + length)..]]);
        await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs004EmptyNativeBackupHasVerifiedZeroCut()
    {
        using var fixture = new NativeBackupCutFixture(empty: true);
        var identity = ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution());
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(identity.DispatchPaused).IsTrue();
        using var restored = new ZoneTreeStore(new(fixture.Destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(restored.Position).IsEqualTo(1L);
        await Assert.That(restored.Read(view => NativeSerialization.Deserialize<bool>(view.ReadOwnedValue(
            KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.DispatchPausedKey))!)))
            .IsTrue();
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs004ExactNativeCutRestoresDataWithNewAuthorityAndPausedDispatch(bool checkpoint)
    {
        using var fixture = new NativeBackupCutFixture(checkpoint);
        Directory.CreateDirectory(fixture.Destination);
        var incarnation = Guid.NewGuid();
        var signingKey = Enumerable.Repeat((byte)1, ZoneTreePersistenceFormat.SigningKeyBytes).ToArray();
        var restoredIdentity = ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(), incarnation, signingKey);
        await Assert.That(restoredIdentity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(restoredIdentity.Incarnation).IsEqualTo(incarnation);
        await Assert.That(restoredIdentity.SigningKey.Span.SequenceEqual(signingKey)).IsTrue();
        await Assert.That(restoredIdentity.DispatchPaused).IsTrue();
        signingKey[0] ^= 1;
        using var restored = new ZoneTreeStore(new(fixture.Destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(restored.Position).IsEqualTo(fixture.Position + 1);
        await Assert.That(restored.Read(view => NativeSerialization.Deserialize<string>(
            view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!))).IsEqualTo(MetadataBackupFixture.ExpectedValue);
        await Assert.That(restored.Read(view => view.ReadOwnedValue(KeyCodec.Encode(
            ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey)))).IsNull();
        await Assert.That(restored.Read(view => view.ReadOwnedValue(KeyCodec.Encode(
            ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.ClockKey)))).IsNull();
        await Assert.That(restored.Identity.SigningKey.Span.SequenceEqual(restoredIdentity.SigningKey.Span)).IsTrue();
    }
}
