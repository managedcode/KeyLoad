using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MetadataTestFiles
{
    internal static async Task PadWithTrailingBytesAsync(string path, int targetLength)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        if (file.Length > targetLength)
        {
            throw new InvalidOperationException(MetadataTestContract.FixtureSizeError);
        }

        var remaining = targetLength - (int)file.Length;
        file.Position = file.Length;
        var whitespace = new byte[Math.Min(MetadataTestContract.PaddingChunkBytes, remaining)];
        Array.Fill(whitespace, MetadataTestContract.TrailingPaddingByte);
        while (remaining > 0)
        {
            var count = Math.Min(remaining, whitespace.Length);
            await file.WriteAsync(whitespace.AsMemory(0, count));
            remaining -= count;
        }
    }

    internal static async Task<ZoneTreeBackupRestoreManifest> ReadManifestAsync(string backupDirectory)
        => ZoneTreeMetadataBinary.Read<ZoneTreeBackupRestoreManifest>(
            await File.ReadAllBytesAsync(Path.Combine(backupDirectory, MetadataTestContract.ManifestFileName)),
            ZoneTreeMetadataBinary.BackupMagic, MetadataTestContract.ManifestUnsupportedDetail);

    internal static Task WriteManifestAsync(string backupDirectory, ZoneTreeBackupRestoreManifest manifest)
        => File.WriteAllBytesAsync(Path.Combine(backupDirectory, MetadataTestContract.ManifestFileName),
            ZoneTreeMetadataBinary.Write(manifest, ZoneTreeMetadataBinary.BackupMagic));

    internal static async Task UpdateManifestFileAsync(string backupDirectory, string fileName)
    {
        var path = Path.Combine(backupDirectory, fileName);
        var manifest = await ReadManifestAsync(backupDirectory);
        await using var file = File.OpenRead(path);
        var replacement = new ZoneTreeBackupRestoreManifestFile(fileName, file.Length,
            Convert.ToHexStringLower(await SHA256.HashDataAsync(file)));
        await WriteManifestAsync(backupDirectory, manifest with
        { Files = manifest.Files.Select(entry => entry.Name == fileName ? replacement : entry).ToArray() });
    }

    internal static async Task BreakIdentityEnvelopeChecksumAsync(string backupDirectory)
    {
        var path = Path.Combine(backupDirectory, MetadataTestContract.IdentityFileName);
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeIdentityEnvelope>(await File.ReadAllBytesAsync(path),
            ZoneTreeMetadataBinary.IdentityMagic, MetadataTestContract.IdentityFormatUnsupportedDetail);
        var checksum = envelope.Checksum.ToArray();
        checksum[0] ^= 1;
        await File.WriteAllBytesAsync(path, ZoneTreeMetadataBinary.Write(
            envelope with { Checksum = checksum }, ZoneTreeMetadataBinary.IdentityMagic));
        await UpdateManifestFileAsync(backupDirectory, MetadataTestContract.IdentityFileName);
    }
}
