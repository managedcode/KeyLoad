namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeFormatUpgradeStage
{
    private const int NoFileAttributes = 0;
    private const int FileStartPosition = 0;

    internal const string SourceCopyDirectory = ".upgrade-source";
    internal const string ReceiptFileName = "format-upgrade.bin";
    internal const string CheckpointTemporaryFileName = ".upgrade-checkpoint.tmp";
    private static readonly HashSet<string> StageEntries = new(StringComparer.Ordinal)
    {
        ReceiptFileName,
        SourceCopyDirectory,
        ZoneTreePersistenceFormat.IdentityFileName,
        ZoneTreePersistenceFormat.IdentityFileName + ZoneTreePersistenceFormat.TemporaryFileSuffix,
        ZoneTreePersistenceFormat.JournalFileName,
        ZoneTreePersistenceFormat.OwnerLockFileName,
        ZoneTreePersistenceFormat.TreeDirectoryName,
        CheckpointTemporaryFileName
    };

    internal static void CreateOrReset(string path, ZoneTreeFormatUpgradeReceipt receipt, ZoneTreeStoreOptions options)
    {
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(path, allowMissingFinal: true);
        if (!Directory.Exists(path))
        {
            ZoneTreeStoreFiles.CreatePrivateDirectory(path);
            ZoneTreeFormatUpgradeReceiptFile.Write(Path.Combine(path, ReceiptFileName), receipt, options.MaximumUpgradeReceiptBytes, options.IdentityBufferBytes);
            return;
        }

        VerifyDirectory(path);
        var receiptPath = Path.Combine(path, ReceiptFileName);
        var existing = ZoneTreeFormatUpgradeReceiptFile.Read(receiptPath, options.MaximumUpgradeReceiptBytes);
        if (existing != receipt)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradeStageMismatch);
        }
        VerifyEntries(path);
        DeleteOwnedEntries(path);
    }

    internal static void CopySources(ZoneTreeFormatUpgradeSource source, string stagePath, ZoneTreeStoreOptions options)
    {
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(source.Directory, allowMissingFinal: false);
        VerifyDirectory(stagePath);
        var copyDirectory = Path.Combine(stagePath, SourceCopyDirectory);
        ZoneTreeStoreFiles.CreatePrivateDirectory(copyDirectory);
        CopyVerified(Path.Combine(source.Directory, ZoneTreePersistenceFormat.IdentityFileName),
            Path.Combine(copyDirectory, ZoneTreePersistenceFormat.IdentityFileName), source.IdentityDigest, options.FileBufferBytes);
        CopyVerified(Path.Combine(source.Directory, ZoneTreePersistenceFormat.JournalFileName),
            Path.Combine(copyDirectory, ZoneTreePersistenceFormat.JournalFileName), source.JournalDigest, options.FileBufferBytes);
    }

    internal static void VerifySourceCopies(string stagePath, ZoneTreeFormatUpgradeSource source, ZoneTreeStoreOptions options)
    {
        VerifyDirectory(stagePath);
        VerifyEntries(stagePath);
        var copyDirectory = Path.Combine(stagePath, SourceCopyDirectory);
        VerifyDirectory(copyDirectory);
        VerifyDigest(Path.Combine(copyDirectory, ZoneTreePersistenceFormat.IdentityFileName), source.IdentityDigest, options.FileBufferBytes);
        VerifyDigest(Path.Combine(copyDirectory, ZoneTreePersistenceFormat.JournalFileName), source.JournalDigest, options.FileBufferBytes);
    }

    internal static string SourceCopyPath(string stagePath, string fileName)
        => Path.Combine(stagePath, SourceCopyDirectory, fileName);

    internal static void RemoveSourceCopies(string stagePath)
    {
        var path = Path.Combine(stagePath, SourceCopyDirectory);
        if (Directory.Exists(path))
        {
            VerifyNoLinks(path);
            VerifySourceCopyEntries(path);
            Directory.Delete(path, recursive: true);
        }
    }

    internal static void VerifyPublishable(string stagePath, ZoneTreeFormatUpgradeReceipt receipt, ZoneTreeStoreOptions options)
    {
        VerifyDirectory(stagePath);
        var actual = ZoneTreeFormatUpgradeReceiptFile.Read(Path.Combine(stagePath, ReceiptFileName), options.MaximumUpgradeReceiptBytes);
        if (actual != receipt || Directory.Exists(Path.Combine(stagePath, SourceCopyDirectory)))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradeStageMismatch);
        }
        VerifyEntries(stagePath);
    }

    internal static void Publish(string stagePath, string destinationPath)
    {
        VerifyDirectory(stagePath);
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(destinationPath, allowMissingFinal: true);
        if (Directory.Exists(destinationPath))
        {
            if (Directory.EnumerateFileSystemEntries(destinationPath).Any())
            {
                throw Errors.Fail(ErrorCode.Conflict, ZoneTreePersistenceFormat.RestoreDestinationNotEmpty);
            }
            Directory.Delete(destinationPath);
        }
        Directory.Move(stagePath, destinationPath);
    }

    internal static void VerifyDirectory(string path)
    {
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(path, allowMissingFinal: false);
        var info = new DirectoryInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradeStageMismatch);
        }
    }

    private static void CopyVerified(string source, string destination, string expectedDigest, int fileBufferBytes)
    {
        ZoneTreeFormatUpgradeReceiptFile.VerifyRegularFile(source);
        File.Copy(source, destination, overwrite: false);
        using var copy = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
            fileBufferBytes, FileOptions.WriteThrough);
        copy.Flush(true);
        copy.Position = FileStartPosition;
        var digest = Digest(copy);
        if (!string.Equals(digest, expectedDigest, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed);
        }
    }

    private static void VerifyDigest(string path, string expectedDigest, int fileBufferBytes)
    {
        ZoneTreeFormatUpgradeReceiptFile.VerifyRegularFile(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
            fileBufferBytes, FileOptions.SequentialScan);
        if (!string.Equals(Digest(file), expectedDigest, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed);
        }
    }

    internal static string Digest(FileStream file)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file));

    private static void VerifyEntries(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            if (!StageEntries.Contains(name))
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradeStageMismatch);
            }
            VerifyNoLinks(entry);
        }
        var source = Path.Combine(path, SourceCopyDirectory);
        if (Directory.Exists(source))
        {
            VerifySourceCopyEntries(source);
        }
    }

    private static void VerifySourceCopyEntries(string path)
    {
        string[] files = [ZoneTreePersistenceFormat.IdentityFileName, ZoneTreePersistenceFormat.JournalFileName];
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (!files.Contains(Path.GetFileName(entry), StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradeStageMismatch);
            }
            ZoneTreeFormatUpgradeReceiptFile.VerifyRegularFile(entry);
        }
    }

    private static void VerifyNoLinks(string entry)
    {
        var pending = new Stack<string>();
        pending.Push(entry);
        while (pending.TryPop(out var path))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != NoFileAttributes)
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradeStageMismatch);
            }
            if ((attributes & FileAttributes.Directory) != NoFileAttributes)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static void DeleteOwnedEntries(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (Path.GetFileName(entry) == ReceiptFileName)
            {
                continue;
            }
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != NoFileAttributes)
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private const string UpgradeStageMismatch = "The offline upgrade staging directory is not owned by this source and target.";
}
