using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class NativeMetadataFormatTests
{
    private const int PrefixBytes = sizeof(ulong);
    private const byte TrailingByte = 0;

    [Test]
    [Arguments(IdentityDefect.WrongType)]
    [Arguments(IdentityDefect.NullRoot)]
    [Arguments(IdentityDefect.NullPayload)]
    [Arguments(IdentityDefect.NullChecksum)]
    [Arguments(IdentityDefect.ChecksumMismatch)]
    [Arguments(IdentityDefect.EmptyNode)]
    [Arguments(IdentityDefect.EmptyIncarnation)]
    [Arguments(IdentityDefect.EmptySigningKey)]
    [Arguments(IdentityDefect.PayloadTrailing)]
    [Arguments(IdentityDefect.FileTrailing)]
    [Arguments(IdentityDefect.Truncated)]
    [Arguments(IdentityDefect.UnsupportedVersion)]
    [Arguments(IdentityDefect.UnsupportedCodec)]
    public async Task AcIs002NativeIdentityDefectsRejectActualBackupBeforeDestinationAndOriginalRestores(IdentityDefect defect)
    {
        using var fixture = new MetadataBackupFixture();
        var path = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        var original = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, IdentityBytes(fixture.OriginalIdentity, defect, original));
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        await AssertRejectedRestore(fixture, defect is IdentityDefect.UnsupportedVersion or IdentityDefect.UnsupportedCodec
            ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
        await File.WriteAllBytesAsync(path, original);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);
    }

    [Test]
    [Arguments(ManifestDefect.WrongType)]
    [Arguments(ManifestDefect.NullRoot)]
    [Arguments(ManifestDefect.NullFiles)]
    [Arguments(ManifestDefect.NullEntry)]
    [Arguments(ManifestDefect.NullChecksum)]
    [Arguments(ManifestDefect.Trailing)]
    [Arguments(ManifestDefect.Truncated)]
    [Arguments(ManifestDefect.UnsupportedVersion)]
    public async Task AcIs002NativeManifestDefectsRejectActualBackupBeforeDestinationAndOriginalRestores(ManifestDefect defect)
    {
        using var fixture = new MetadataBackupFixture();
        var path = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestFileName);
        var original = await File.ReadAllBytesAsync(path);
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.BackupDirectory);
        await File.WriteAllBytesAsync(path, ManifestBytes(manifest, defect, original));
        await AssertRejectedRestore(fixture, defect == ManifestDefect.UnsupportedVersion
            ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
        await File.WriteAllBytesAsync(path, original);
        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);
    }

    [Test]
    [Arguments(CheckpointDefect.WrongType)]
    [Arguments(CheckpointDefect.NullRoot)]
    [Arguments(CheckpointDefect.EmptyIncarnation)]
    [Arguments(CheckpointDefect.UnsupportedVersion)]
    [Arguments(CheckpointDefect.UnsupportedCodec)]
    [Arguments(CheckpointDefect.Trailing)]
    [Arguments(CheckpointDefect.Truncated)]
    public async Task AcIs002NativeCheckpointDefectsRejectBeforeLiveStateAndOriginalInstalls(CheckpointDefect defect)
    {
        using var fixture = new MetadataBackupFixture();
        using var store = new ZoneTreeStore(new(fixture.SourceDirectory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        store.CreateSnapshot(fixture.RestoredDirectory);
        var original = await File.ReadAllBytesAsync(fixture.RestoredDirectory);
        var length = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset));
        var metadata = NativeSerialization.Deserialize<ZoneTreeCheckpointMetadata>(
            original.AsSpan(WalFileFixture.HeaderBytes, length));
        var payload = CheckpointPayload(metadata, defect);
        var frame = WalFileFixture.CreateFrame(payload, metadata.Position, ZoneTreePersistenceFormat.CheckpointMagic);
        await File.WriteAllBytesAsync(fixture.RestoredDirectory,
            [.. frame, .. original[(WalFileFixture.HeaderBytes + length)..]]);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => store.InstallSnapshot(fixture.RestoredDirectory, 0));
        await Assert.That(failure.Code).IsEqualTo(defect is CheckpointDefect.UnsupportedVersion or CheckpointDefect.UnsupportedCodec
            ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
        await Assert.That(store.Position).IsEqualTo(metadata.Position);
        await Assert.That(store.Read(view => view.GetRecord<string>(MetadataBackupFixture.StoredKeyBytes)))
            .IsEqualTo(MetadataBackupFixture.ExpectedValue);
        await File.WriteAllBytesAsync(fixture.RestoredDirectory, original);
        await Assert.That(store.InstallSnapshot(fixture.RestoredDirectory, 0).RecordCount).IsEqualTo(1L);
    }

    private static byte[] IdentityBytes(StoreIdentity identity, IdentityDefect defect, byte[] original)
    {
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeIdentityEnvelope>(original,
            ZoneTreeMetadataBinary.IdentityMagic, MetadataTestContract.IdentityFormatUnsupportedDetail);
        return defect switch
        {
            IdentityDefect.WrongType => NativeFile(new ZoneTreeCheckpointMetadata(ZoneTreePersistenceFormat.CheckpointVersion, KeyCodec.Version, identity.Incarnation, 0, 0), ZoneTreeMetadataBinary.IdentityMagic),
            IdentityDefect.NullRoot => NativeFile<ZoneTreeIdentityEnvelope>(null!, ZoneTreeMetadataBinary.IdentityMagic),
            IdentityDefect.NullPayload => NativeFile(envelope with { Payload = null! }, ZoneTreeMetadataBinary.IdentityMagic),
            IdentityDefect.NullChecksum => NativeFile(envelope with { Checksum = null! }, ZoneTreeMetadataBinary.IdentityMagic),
            IdentityDefect.ChecksumMismatch => NativeFile(envelope with { Checksum = TamperChecksum(envelope.Checksum) }, ZoneTreeMetadataBinary.IdentityMagic),
            IdentityDefect.EmptyNode => EncodeIdentity(identity with { NodeId = Guid.Empty }),
            IdentityDefect.EmptyIncarnation => EncodeIdentity(identity with { Incarnation = Guid.Empty }),
            IdentityDefect.EmptySigningKey => EncodeIdentity(identity with { SigningKey = ReadOnlyMemory<byte>.Empty }),
            IdentityDefect.PayloadTrailing => EncodeEnvelope([.. NativeSerialization.Serialize(identity), TrailingByte]),
            IdentityDefect.FileTrailing => [.. original, TrailingByte],
            IdentityDefect.Truncated => original[..^MetadataTestContract.OneByte],
            IdentityDefect.UnsupportedVersion => EncodeIdentity(identity with { FormatVersion = ZoneTreePersistenceFormat.CurrentDataEpoch + MetadataTestContract.OneByte }),
            IdentityDefect.UnsupportedCodec => EncodeIdentity(identity with { KeyCodecVersion = KeyCodec.Version + MetadataTestContract.OneByte }),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
    }

    private static byte[] ManifestBytes(ZoneTreeBackupRestoreManifest manifest, ManifestDefect defect, byte[] original)
        => defect switch
        {
            ManifestDefect.WrongType => NativeFile(new ZoneTreeIdentityEnvelope([], []), ZoneTreeMetadataBinary.BackupMagic),
            ManifestDefect.NullRoot => NativeFile<ZoneTreeBackupRestoreManifest>(null!, ZoneTreeMetadataBinary.BackupMagic),
            ManifestDefect.NullFiles => NativeFile(manifest with { Files = null! }, ZoneTreeMetadataBinary.BackupMagic),
            ManifestDefect.NullEntry => NativeFile(manifest with { Files = [null!, manifest.Files[1]] }, ZoneTreeMetadataBinary.BackupMagic),
            ManifestDefect.NullChecksum => NativeFile(manifest with { Files = [manifest.Files[0] with { Checksum = null! }, manifest.Files[1]] }, ZoneTreeMetadataBinary.BackupMagic),
            ManifestDefect.Trailing => [.. original, TrailingByte],
            ManifestDefect.Truncated => original[..^MetadataTestContract.OneByte],
            ManifestDefect.UnsupportedVersion => NativeFile(manifest with { Version = MetadataTestContract.UnsupportedManifestVersion }, ZoneTreeMetadataBinary.BackupMagic),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

    private static byte[] CheckpointPayload(ZoneTreeCheckpointMetadata metadata, CheckpointDefect defect)
        => defect switch
        {
            CheckpointDefect.WrongType => RawPayload(new ZoneTreeIdentityEnvelope([], [])),
            CheckpointDefect.NullRoot => RawPayload<ZoneTreeCheckpointMetadata>(null!),
            CheckpointDefect.EmptyIncarnation => RawPayload(metadata with { Incarnation = Guid.Empty }),
            CheckpointDefect.UnsupportedVersion => RawPayload(metadata with { Version = ZoneTreePersistenceFormat.CheckpointVersion + MetadataTestContract.OneByte }),
            CheckpointDefect.UnsupportedCodec => RawPayload(metadata with { CodecVersion = KeyCodec.Version + MetadataTestContract.OneByte }),
            CheckpointDefect.Trailing => [.. RawPayload(metadata), TrailingByte],
            CheckpointDefect.Truncated => RawPayload(metadata)[..^MetadataTestContract.OneByte],
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

    private static byte[] TamperChecksum(byte[] checksum)
    {
        var changed = checksum.ToArray();
        changed[0] ^= MetadataTestContract.OneByte;
        return changed;
    }

    private static byte[] EncodeIdentity(StoreIdentity identity) => EncodeEnvelope(NativeSerialization.Serialize(identity));
    private static byte[] EncodeEnvelope(byte[] payload)
        => NativeFile(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)), ZoneTreeMetadataBinary.IdentityMagic);

    // Direct official codecs deliberately bypass required-member validation solely to encode corruption fixtures.
    private static byte[] RawPayload<T>(T value)
    {
        var registrations = new ServiceCollection();
        registrations.AddSerializer(builder => builder.AddAssembly(typeof(NativeSerialization).Assembly)
            .AddAssembly(typeof(T).Assembly).AddAssembly(typeof(StoreIdentity).Assembly));
        registrations.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        using var services = registrations.BuildServiceProvider();
        return services.GetRequiredService<Serializer<NativePayload>>().SerializeToArray(
            new NativePayload { Version = NativePayloadVersion.Current, Value = value });
    }

    private static byte[] NativeFile<T>(T value, ulong magic)
    {
        var payload = RawPayload(value);
        var bytes = new byte[PrefixBytes + payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, magic);
        payload.CopyTo(bytes, PrefixBytes);
        return bytes;
    }

    private static async Task AssertRejectedRestore(MetadataBackupFixture fixture, ErrorCode expected)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(fixture.BackupDirectory, fixture.RestoredDirectory, UnitExecutionOptions.StorageExecution()));
        await Assert.That(failure.Code).IsEqualTo(expected);
        await Assert.That(Directory.Exists(fixture.RestoredDirectory)).IsFalse();
    }

    internal enum IdentityDefect { WrongType, NullRoot, NullPayload, NullChecksum, ChecksumMismatch, EmptyNode, EmptyIncarnation, EmptySigningKey, PayloadTrailing, FileTrailing, Truncated, UnsupportedVersion, UnsupportedCodec }
    internal enum ManifestDefect { WrongType, NullRoot, NullFiles, NullEntry, NullChecksum, Trailing, Truncated, UnsupportedVersion }
    internal enum CheckpointDefect { WrongType, NullRoot, EmptyIncarnation, UnsupportedVersion, UnsupportedCodec, Trailing, Truncated }
}
