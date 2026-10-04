using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupRestoreFiles
{
    private static readonly string[] SourceFiles = [IdentityFileName, JournalFileName];

    internal static long Create(ZoneTreeStoreRuntime runtime, string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, BackupDestinationNotEmpty);
        }

        ZoneTreeStoreFiles.CreatePrivateDirectory(directory);
        runtime.Journal.Flush(true);
        var files = new ZoneTreeBackupRestoreManifestFile[SourceFiles.Length];
        for (var index = 0; index < SourceFiles.Length; index++)
        {
            files[index] = CopyAndDescribe(runtime.Options.Directory, directory, SourceFiles[index]);
        }

        WriteManifest(directory, new(BackupManifestVersion, runtime.Position, files));
        return runtime.Position;
    }

    internal static StoreIdentity ReadAndVerify(string backup, string staging)
    {
        var manifestBytes = ZoneTreeMetadataFile.Read(Path.Combine(backup, BackupManifestFileName),
            MaximumBackupManifestBytes, BackupManifestUnsupported);
        var manifest = ZoneTreeMetadataBinary.Read<ZoneTreeBackupRestoreManifest>(manifestBytes.Span, ZoneTreeMetadataBinary.BackupMagic, BackupManifestUnsupported);
        if (manifest.Version != BackupManifestVersion || manifest.Position < 0 || manifest.Files.Length != SourceFiles.Length
            || !manifest.Files.Select(file => file.Name).Order().SequenceEqual(SourceFiles.Order()))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, BackupManifestUnsupported);
        }

        ReadOnlyMemory<byte> identityBytes = default;
        foreach (var item in manifest.Files)
        {
            if (item.Name == IdentityFileName)
            {
                identityBytes = ReadAndVerifyIdentityFile(backup, item);
            }
            else
            {
                CopyAndVerifyJournal(backup, staging, item);
            }
        }

        var identity = ZoneTreeIdentityFile.Read(identityBytes.Span);
        using var journal = File.OpenRead(Path.Combine(staging, JournalFileName));
        ZoneTreeBackupJournalValidation.Verify(journal, identity, manifest.Position);
        return identity;
    }

    private static ZoneTreeBackupRestoreManifestFile CopyAndDescribe(string sourceDirectory, string backupDirectory,
        string name)
    {
        var source = Path.Combine(sourceDirectory, name);
        var destination = Path.Combine(backupDirectory, name);
        File.Copy(source, destination, false);
        using var copied = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        copied.Flush(true);
        copied.Position = 0;
        return new(name, copied.Length, Convert.ToHexStringLower(SHA256.HashData(copied)));
    }

    private static void WriteManifest(string directory, ZoneTreeBackupRestoreManifest manifest)
    {
        using var file = new FileStream(Path.Combine(directory, BackupManifestFileName),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(ZoneTreeMetadataBinary.Write(manifest, ZoneTreeMetadataBinary.BackupMagic));
        file.Flush(true);
    }

    private static void CopyAndVerifyJournal(string backup, string staging, ZoneTreeBackupRestoreManifestFile item)
    {
        var source = Path.Combine(backup, item.Name);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileIsLink);
        }

        using var sourceFile = File.OpenRead(source);
        if (sourceFile.Length != item.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
        using var file = new FileStream(Path.Combine(staging, JournalFileName),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        CopyExactBytes(sourceFile, file, item.Length);
        file.Flush(true);
        file.Position = 0;
        if (file.Length != item.Length
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(file)), item.Checksum, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
    }

    private static void CopyExactBytes(FileStream source, FileStream destination, long remaining)
    {
        var buffer = new byte[FileBufferBytes];
        while (remaining > 0)
        {
            var read = source.Read(buffer.AsSpan(0, (int)Math.Min(remaining, buffer.Length)));
            if (read == 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
            }
            destination.Write(buffer.AsSpan(0, read));
            remaining -= read;
        }
        if (source.ReadByte() != -1)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
    }

    private static ReadOnlyMemory<byte> ReadAndVerifyIdentityFile(string backup,
        ZoneTreeBackupRestoreManifestFile item)
    {
        var source = Path.Combine(backup, item.Name);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileIsLink);
        }

        var bytes = ZoneTreeMetadataFile.Read(source, MaximumIdentityFileBytes, IdentityFormatUnsupported);
        if (bytes.Length != item.Length
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes.Span)), item.Checksum,
                StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }

        return bytes;
    }
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.BackupManifest)]
internal sealed record ZoneTreeBackupRestoreManifest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] long Position,
    [property: global::Orleans.Id(2)] ZoneTreeBackupRestoreManifestFile[] Files);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.BackupFile)]
internal sealed record ZoneTreeBackupRestoreManifestFile(
    [property: global::Orleans.Id(0)] string Name,
    [property: global::Orleans.Id(1)] long Length,
    [property: global::Orleans.Id(2)] string Checksum);
