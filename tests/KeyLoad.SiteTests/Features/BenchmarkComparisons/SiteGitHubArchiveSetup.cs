using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteGitHubArchiveSetup
{
    public static Task<SiteGitHubArchiveReceipt> PrepareAsync(
        string archivePath,
        string receiptPath,
        string destination,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        ValidatePaths(archivePath, receiptPath, destination, evidenceRoot);
        archivePath = Path.GetFullPath(archivePath);
        receiptPath = Path.GetFullPath(receiptPath);
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        evidenceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot));
        return SiteGitHubArchiveReader.ExtractAsync(
            archivePath, receiptPath, destination, evidenceRoot, cancellationToken);
    }

    public static Task<SiteGitHubArchiveReceipt> PrepareFromEnvironmentAsync(
        CancellationToken cancellationToken)
    {
        var archivePath = RequiredEnvironmentPath(SiteGitHubArchiveTokens.ArchiveEnvironment);
        var receiptPath = RequiredEnvironmentPath(SiteGitHubArchiveTokens.ArchiveReceiptEnvironment);
        var destination = RequiredEnvironmentPath(SiteGitHubArchiveTokens.ReportsEnvironment);
        var evidenceRoot = RequiredEnvironmentPath(SiteGitHubArchiveTokens.CoverageEnvironment);
        return PrepareAsync(archivePath, receiptPath, destination, evidenceRoot, cancellationToken);
    }

    public static Task VerifyUnchangedAsync(
        SiteGitHubArchiveReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var archivePath = RequiredEnvironmentPath(SiteGitHubArchiveTokens.ArchiveEnvironment);
        return SiteGitHubArchiveVerifier.VerifyUnchangedAsync(archivePath, receipt, cancellationToken);
    }

    private static void ValidatePaths(string archivePath, string receiptPath, string destination, string evidenceRoot)
    {
        if (!Path.IsPathFullyQualified(archivePath) || !Path.IsPathFullyQualified(receiptPath) ||
            !Path.IsPathFullyQualified(destination) || !Path.IsPathFullyQualified(evidenceRoot))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.AbsolutePathRequired);
        }

        if (!File.Exists(archivePath))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.ArchiveMissing);
        }

        if (!File.Exists(receiptPath))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.ReceiptMissing);
        }

        if (!Directory.Exists(evidenceRoot))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.CoverageRootMissing);
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.ExistingDestination);
        }
    }

    private static string RequiredEnvironmentPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(name == SiteGitHubArchiveTokens.CoverageEnvironment
                ? SiteGitHubArchiveTokens.CoverageRootMissing
                : SiteGitHubArchiveTokens.ArchiveInputsMissing);
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.AbsolutePathRequired);
        }

        return path;
    }
}

internal static class SiteGitHubArchiveVerifier
{
    public static async Task VerifyUnchangedAsync(
        string archivePath,
        SiteGitHubArchiveReceipt receipt,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        SiteGitHubArchiveZip.EnsureArchiveBound(stream.Length);
        var digest = await SiteGitHubArchiveFileOperations.HashStreamAsync(stream, cancellationToken);
        SiteGitHubArchiveFileOperations.EnsureArchiveIdentity(digest, stream.Length, receipt.Archive);
        await VerifyInputsAsync(stream, receipt, cancellationToken);
        var finalDigest = await SiteGitHubArchiveFileOperations.HashStreamAsync(stream, cancellationToken);
        SiteGitHubArchiveFileOperations.EnsureArchiveIdentity(finalDigest, stream.Length, receipt.Archive);
    }

    private static async Task VerifyInputsAsync(
        FileStream stream,
        SiteGitHubArchiveReceipt receipt,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var inputs = receipt.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var expected = SiteGitHubArchiveZip.ExpectedPaths();
        var files = archive.Entries.Where(entry => !entry.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal)).ToArray();
        if (receipt.SchemaVersion != SiteGitHubArchiveTokens.SchemaVersionNumber ||
            !Path.IsPathFullyQualified(receipt.ReportsRoot) ||
            files.Length != SiteGitHubArchiveTokens.ExpectedArchiveFileCount || inputs.Count != files.Length ||
            !expected.SetEquals(inputs.Keys))
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
        }

        long total = SiteGitHubArchiveTokens.Zero;
        foreach (var entry in files)
        {
            if (!inputs.TryGetValue(entry.FullName, out var expectedFile))
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            var actual = await SiteGitHubArchiveFileOperations.HashEntryAsync(
                entry, expectedFile.Bytes, cancellationToken);
            if (actual.Bytes != expectedFile.Bytes || actual.Sha256 != expectedFile.Sha256)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            total = checked(total + actual.Bytes);
            if (total > SiteGitHubArchiveTokens.MaximumUncompressedBytes)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            var output = await SiteGitHubArchiveFileOperations.HashExtractedFileAsync(
                receipt.ReportsRoot, expectedFile, cancellationToken);
            if (output.Bytes != expectedFile.Bytes || output.Sha256 != expectedFile.Sha256)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }
        }
    }
}

internal static class SiteGitHubArchiveZip
{
    private static readonly string[] Profiles =
    [
        SiteGitHubArchiveTokens.SmokeProfile,
        SiteGitHubArchiveTokens.SmallProfile,
        SiteGitHubArchiveTokens.LargeProfile,
    ];

    private static readonly string[] FileNames =
    [
        SiteGitHubArchiveTokens.ResultsJson,
        SiteGitHubArchiveTokens.SamplesCsv,
        SiteGitHubArchiveTokens.ResultsMarkdown,
        SiteGitHubArchiveTokens.RunnerLog,
    ];

    public static async Task<SiteGitHubArchiveFileReceipt[]> ExtractIntoStagingAsync(
        FileStream stream,
        string staging,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = await PreflightAsync(archive, cancellationToken);
            Directory.CreateDirectory(staging);
            return await SiteGitHubArchiveFileOperations.ExtractEntriesAsync(entries, staging, cancellationToken);
        }
        catch (InvalidDataException exception) when (!IsKnownArchiveFailure(exception.Message))
        {
            DeleteStaging(staging);
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchive, exception);
        }
        catch (Exception exception) when (IsArchiveOperationFailure(exception))
        {
            DeleteStaging(staging);
            throw;
        }
    }

    public static void EnsureArchiveBound(long bytes)
    {
        if (bytes > SiteGitHubArchiveTokens.MaximumArchiveBytes)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.ArchiveTooLarge);
        }
    }

    internal static HashSet<string> ExpectedPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            foreach (var file in FileNames)
            {
                paths.Add(profile + SiteGitHubArchiveTokens.ProfileSeparator + file);
            }
        }

        return paths;
    }

    private static async Task<List<SiteGitHubArchiveEntryReceipt>> PreflightAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var expected = ExpectedPaths();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var receipts = new List<SiteGitHubArchiveEntryReceipt>(SiteGitHubArchiveTokens.ExpectedArchiveFileCount);
        long total = SiteGitHubArchiveTokens.Zero;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryPath(entry.FullName, seen);
            if (entry.FullName.EndsWith(SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal))
            {
                await ValidateDirectoryAsync(entry, cancellationToken);
                continue;
            }

            if (!expected.Contains(entry.FullName))
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidEntry);
            }

            ValidateFileType(entry);
            EnsureEntryBound(entry.Length);
            total = checked(total + entry.Length);
            if (total > SiteGitHubArchiveTokens.MaximumUncompressedBytes)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.TotalTooLarge);
            }

            var digest = await SiteGitHubArchiveFileOperations.HashEntryAsync(entry, entry.Length, cancellationToken);
            receipts.Add(new(entry.FullName, digest.Sha256, digest.Bytes, entry));
        }

        if (receipts.Count != SiteGitHubArchiveTokens.ExpectedArchiveFileCount ||
            expected.Except(receipts.Select(file => file.Path), StringComparer.Ordinal).Any())
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.IncompleteArchive);
        }

        return receipts;
    }

    private static void ValidateEntryPath(string path, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(path) || path.Contains(SiteGitHubArchiveTokens.Backslash, StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Split(SiteGitHubArchiveTokens.ProfileSeparator).Any(segment =>
                segment is SiteGitHubArchiveTokens.DotSegment or SiteGitHubArchiveTokens.ParentSegment))
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidEntry);
        }

        if (!seen.Add(path))
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.DuplicateEntry);
        }
    }

    private static async Task ValidateDirectoryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        ValidateDirectoryType(entry);
        var isProfileDirectory = Profiles.Any(profile => string.Equals(
            entry.FullName, profile + SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal));
        if (!isProfileDirectory || entry.Length != SiteGitHubArchiveTokens.Zero)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidDirectory);
        }

        await using var directory = await entry.OpenAsync(cancellationToken);
        var probe = new byte[SiteGitHubArchiveTokens.One];
        if (await directory.ReadAsync(probe, cancellationToken) != SiteGitHubArchiveTokens.Zero)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidDirectory);
        }
    }

    private static void ValidateDirectoryType(ZipArchiveEntry entry)
    {
        var attributes = (FileAttributes)entry.ExternalAttributes;
        var unixType = ReadUnixType(entry);
        var validUnixType = unixType == SiteGitHubArchiveTokens.Zero ||
                            unixType == SiteGitHubArchiveTokens.UnixDirectoryType;
        if (!validUnixType || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidDirectory);
        }
    }

    private static void ValidateFileType(ZipArchiveEntry entry)
    {
        var attributes = (FileAttributes)entry.ExternalAttributes;
        var unixType = ReadUnixType(entry);
        var isLink = unixType == SiteGitHubArchiveTokens.UnixSymbolicLinkType ||
                     (attributes & FileAttributes.ReparsePoint) != 0;
        var isSpecial = (unixType != SiteGitHubArchiveTokens.Zero &&
                         unixType != SiteGitHubArchiveTokens.UnixRegularFileType) ||
                        (attributes & FileAttributes.Directory) != 0;
        if (isLink || isSpecial)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidEntry);
        }
    }

    private static int ReadUnixType(ZipArchiveEntry entry) =>
        (entry.ExternalAttributes >> SiteGitHubArchiveTokens.UnixTypeShift) & SiteGitHubArchiveTokens.UnixTypeMask;

    private static void EnsureEntryBound(long bytes)
    {
        if (bytes > SiteGitHubArchiveTokens.MaximumEntryBytes)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.EntryTooLarge);
        }
    }

    private static bool IsKnownArchiveFailure(string message) =>
        message is SiteGitHubArchiveTokens.InvalidEntry or SiteGitHubArchiveTokens.DuplicateEntry or
            SiteGitHubArchiveTokens.InvalidDirectory or SiteGitHubArchiveTokens.EntryTooLarge or
            SiteGitHubArchiveTokens.TotalTooLarge or SiteGitHubArchiveTokens.StreamBoundsExceeded or
            SiteGitHubArchiveTokens.IncompleteArchive or SiteGitHubArchiveTokens.InvalidArchiveReceipt or
            SiteGitHubArchiveTokens.InvalidArchive;

    private static bool IsArchiveOperationFailure(Exception exception) =>
        exception is InvalidDataException || exception is IOException || exception is UnauthorizedAccessException ||
        exception is OperationCanceledException || exception is ArgumentException ||
        exception is InvalidOperationException || exception is OverflowException ||
        exception is CryptographicException || exception is NotSupportedException || exception is SecurityException;

    private static void DeleteStaging(string staging)
    {
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }
    }
}
