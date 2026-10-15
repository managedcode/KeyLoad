using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupRestoreFiles
{
    private const int FirstSourceFileIndex = 0;
    private const int InitialJournalPosition = 0;
    private const int FileStartPosition = 0;
    private const int NoFileAttributes = 0;
    private const int NoRemainingBytes = 0;
    private const int FirstBufferByte = 0;
    private const int NoReadBytes = 0;
    private const int EndOfStream = -1;

    private static readonly string[] SourceFiles = [IdentityFileName, JournalFileName];

    internal static long Create(ZoneTreeStoreRuntime runtime, string directory, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, BackupDestinationNotEmpty);
        }

        ZoneTreeCatalogBackupPublication.RequirePath(directory);
        cancellationToken.ThrowIfCancellationRequested();
        ZoneTreeStoreFiles.CreatePrivateDirectory(directory);
        runtime.Journal.Flush(true);
        cancellationToken.ThrowIfCancellationRequested();
        var files = new ZoneTreeBackupRestoreManifestFile[SourceFiles.Length];
        for (var index = FirstSourceFileIndex; index < SourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            files[index] = CopyAndDescribe(runtime.Options.Directory, directory, SourceFiles[index],
                runtime.Options.FileBufferBytes, runtime.Options.StreamBufferBytes, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        WriteManifest(directory, new(BackupManifestVersion, runtime.Position, files), runtime.Options.StreamBufferBytes);
        return runtime.Position;
    }

    internal static StoreIdentity ReadAndVerify(string backup, string staging, ZoneTreeStorageExecutionOptions policy)
        => ReadAndVerify(backup, staging, policy, out _);

    internal static StoreIdentity ReadAndVerify(string backup, string staging, ZoneTreeStorageExecutionOptions policy,
        out long position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = ReadManifestDefinition(backup, policy);

        ReadOnlyMemory<byte> identityBytes = default;
        foreach (var item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Name == IdentityFileName)
            {
                identityBytes = ReadAndVerifyIdentityFile(backup, item, policy.MaximumIdentityFileBytes, policy.StreamBufferBytes);
            }
            else
            {
                CopyAndVerifyJournal(backup, staging, item, policy.FileBufferBytes, policy.StreamBufferBytes, cancellationToken);
            }
        }

        position = manifest.Position;
        var identity = ZoneTreeIdentityFile.Read(identityBytes.Span);
        using var journal = new FileStream(Path.Combine(staging, JournalFileName), FileMode.Open, FileAccess.Read,
            FileShare.Read, policy.StreamBufferBytes);
        ZoneTreeBackupJournalValidation.Verify(journal, identity, manifest.Position, policy, cancellationToken);
        return identity;
    }

    internal static ZoneTreeBackupRestoreManifest ReadManifestDefinition(string backup, ZoneTreeStorageExecutionOptions policy)
    {
        var manifestBytes = ReadManifest(backup, policy);
        var manifest = ZoneTreeMetadataBinary.Read<ZoneTreeBackupRestoreManifest>(manifestBytes.Span, ZoneTreeMetadataBinary.BackupMagic, BackupManifestUnsupported);
        if (manifest.Version != BackupManifestVersion || manifest.Position < InitialJournalPosition || manifest.Files.Length != SourceFiles.Length
            || !manifest.Files.Select(file => file.Name).Order().SequenceEqual(SourceFiles.Order()))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, BackupManifestUnsupported);
        }

        return manifest;
    }

    private static ReadOnlyMemory<byte> ReadManifest(string backup, ZoneTreeStorageExecutionOptions policy)
    {
        try
        {
            return ZoneTreeMetadataFile.Read(Path.Combine(backup, BackupManifestFileName),
                policy.MaximumBackupManifestBytes, policy.StreamBufferBytes, BackupManifestUnsupported);
        }
        catch (FileNotFoundException)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, BackupManifestUnsupported);
        }
        catch (DirectoryNotFoundException)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, BackupManifestUnsupported);
        }
    }

    private static ZoneTreeBackupRestoreManifestFile CopyAndDescribe(string sourceDirectory, string backupDirectory,
        string name, int fileBufferBytes, int streamBufferBytes, CancellationToken cancellationToken)
    {
        var source = Path.Combine(sourceDirectory, name);
        var destination = Path.Combine(backupDirectory, name);
        using var original = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, streamBufferBytes);
        using var copied = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, streamBufferBytes);
        var buffer = new byte[fileBufferBytes];
        CopyExactBytes(original, copied, original.Length, buffer, cancellationToken);
        copied.Flush(true);
        copied.Position = FileStartPosition;
        return new(name, copied.Length, ComputeChecksum(copied, buffer, cancellationToken));
    }

    private static void WriteManifest(string directory, ZoneTreeBackupRestoreManifest manifest, int streamBufferBytes)
    {
        using var file = new FileStream(Path.Combine(directory, BackupManifestFileName),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, streamBufferBytes);
        file.Write(ZoneTreeMetadataBinary.Write(manifest, ZoneTreeMetadataBinary.BackupMagic));
        file.Flush(true);
    }

    private static void CopyAndVerifyJournal(string backup, string staging, ZoneTreeBackupRestoreManifestFile item,
        int fileBufferBytes, int streamBufferBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Path.Combine(backup, item.Name);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileIsLink);
        }

        using var sourceFile = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, streamBufferBytes);
        if (sourceFile.Length != item.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
        using var file = new FileStream(Path.Combine(staging, JournalFileName),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, streamBufferBytes);
        var buffer = new byte[fileBufferBytes];
        CopyExactBytes(sourceFile, file, item.Length, buffer, cancellationToken);
        file.Flush(true);
        file.Position = FileStartPosition;
        if (file.Length != item.Length
            || !string.Equals(ComputeChecksum(file, buffer, cancellationToken), item.Checksum, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
    }

    private static void CopyExactBytes(FileStream source, FileStream destination, long remaining, byte[] buffer, CancellationToken cancellationToken)
    {
        while (remaining > NoRemainingBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer.AsSpan(FirstBufferByte, (int)Math.Min(remaining, buffer.Length)));
            if (read == NoReadBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
            }
            destination.Write(buffer.AsSpan(FirstBufferByte, read));
            remaining -= read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (source.ReadByte() != EndOfStream)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
    }

    private static string ComputeChecksum(FileStream file, byte[] buffer, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = file.Read(buffer);
            if (count == NoReadBytes)
            {
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            hash.AppendData(buffer.AsSpan(FirstBufferByte, count));
        }
    }

    internal static ReadOnlyMemory<byte> ReadAndVerifyIdentityFile(string backup,
        ZoneTreeBackupRestoreManifestFile item, int maximumIdentityFileBytes, int streamBufferBytes)
    {
        var source = Path.Combine(backup, item.Name);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileIsLink);
        }

        var bytes = ZoneTreeMetadataFile.Read(source, maximumIdentityFileBytes, streamBufferBytes, IdentityFormatUnsupported);
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
