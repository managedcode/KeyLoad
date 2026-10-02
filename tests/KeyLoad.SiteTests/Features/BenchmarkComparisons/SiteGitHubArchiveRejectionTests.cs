using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteGitHubArchiveRejectionTests
{
    [Test]
    public async Task AC_BC_028_UnsafeDuplicateAndUnexpectedEntriesAreRejectedBeforeOutputCreation()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var cases = SiteGitHubArchiveMutation.PathCases;
        foreach (var item in cases)
        {
            var archive = Path.Combine(temporary.Path, item.FileName);
            await SiteGitHubArchiveMutation.CreateAsync(inputs.ArchivePath, archive, item.Kind, token);
            var receipt = await SiteGitHubArchiveMutation.CreateMatchingReceiptAsync(inputs.ReceiptPath, archive,
                Path.Combine(temporary.Path, SiteGitHubArchiveTokens.ValidReceiptMutationFile), token);
            var destination = Path.Combine(temporary.Path, item.DestinationName);
            var evidenceRoot = Path.Combine(temporary.Path, item.EvidenceName);
            await SiteGitHubArchiveRejectionAssertions.AssertRejectedAsync(
                archive, receipt, destination, evidenceRoot, item.ExpectedError, token);
        }
    }

    [Test]
    public async Task AC_BC_028_NonemptyDirectoryAndDeclaredSizeBoundsAreRejected()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var cases = SiteGitHubArchiveMutation.BoundCases;
        foreach (var item in cases)
        {
            var archive = Path.Combine(temporary.Path, item.FileName);
            await SiteGitHubArchiveMutation.CreateAsync(inputs.ArchivePath, archive, item.Kind, token);
            var receipt = await SiteGitHubArchiveMutation.CreateMatchingReceiptAsync(inputs.ReceiptPath, archive,
                Path.Combine(temporary.Path, SiteGitHubArchiveTokens.ValidReceiptMutationFile), token);
            var destination = Path.Combine(temporary.Path, item.DestinationName);
            var evidenceRoot = Path.Combine(temporary.Path, item.EvidenceName);
            await SiteGitHubArchiveRejectionAssertions.AssertRejectedAsync(
                archive, receipt, destination, evidenceRoot, item.ExpectedError, token);
        }
    }

    [Test]
    public async Task AC_BC_028_ArchiveBoundCorruptionMissingAndExistingOutputFailClosed()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var oversized = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.ArchiveTooLargeFile);
        await SiteGitHubArchiveRawZipMutation.CreateOversizedArchiveAsync(inputs.ArchivePath, oversized, token);
        var oversizedReceipt = await SiteGitHubArchiveMutation.CreateMatchingReceiptAsync(inputs.ReceiptPath,
            oversized, Path.Combine(temporary.Path, SiteGitHubArchiveTokens.ValidReceiptMutationFile), token);
        await SiteGitHubArchiveRejectionAssertions.AssertRejectedAsync(oversized, oversizedReceipt,
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.MaximumArchiveDirectory),
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.EvidenceName),
            SiteGitHubArchiveTokens.ArchiveTooLarge, token);

        var corrupt = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.CorruptArchiveFile);
        await SiteGitHubArchiveRawZipMutation.CreateCorruptArchiveAsync(inputs.ArchivePath, corrupt, token);
        var corruptReceipt = await SiteGitHubArchiveMutation.CreateMatchingReceiptAsync(inputs.ReceiptPath,
            corrupt, Path.Combine(temporary.Path, SiteGitHubArchiveTokens.ValidReceiptMutationFile), token);
        await SiteGitHubArchiveRejectionAssertions.AssertRejectedAsync(corrupt, corruptReceipt,
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.MutationOutputDirectory),
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.EvidenceName),
            SiteGitHubArchiveTokens.InvalidArchive, token);

        var missing = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.MissingArchiveFile);
        await SiteGitHubArchiveRejectionAssertions.AssertThrowsMessageAsync<InvalidOperationException>(
            missing, inputs.ReceiptPath,
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.MissingOutputDirectory),
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.EvidenceName), SiteGitHubArchiveTokens.ArchiveMissing, token);
        await AssertExistingDestinationPreserved(inputs, temporary.Path, token);
    }

    [Test]
    public async Task AC_BC_028_AuthenticArchiveRejectsInconsistentReceiptModes()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        await SiteGitHubArchiveRejectionAssertions.AssertInvalidModeAsync(
            inputs, temporary.Path, SiteGitHubArchiveTokens.Validate, true,
            SiteGitHubArchiveTokens.ValidateEligibleReceipt, SiteGitHubArchiveTokens.ValidateEligibleOutput, token);
        await SiteGitHubArchiveRejectionAssertions.AssertInvalidModeAsync(
            inputs, temporary.Path, SiteGitHubArchiveTokens.Publish, false,
            SiteGitHubArchiveTokens.PublishIneligibleReceipt, SiteGitHubArchiveTokens.PublishIneligibleOutput, token);
        await SiteGitHubArchiveRejectionAssertions.AssertInvalidModeAsync(
            inputs, temporary.Path, SiteGitHubArchiveTokens.UnsupportedMode, false,
            SiteGitHubArchiveTokens.UnknownModeReceipt, SiteGitHubArchiveTokens.UnknownModeOutput, token);
    }

    [Test]
    public async Task AC_BC_028_AuthenticArchiveRejectsInvalidReceiptDigestBounds()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        await SiteGitHubArchiveRejectionAssertions.AssertInvalidDigestAsync(
            inputs, temporary.Path, SiteGitHubArchiveTokens.UppercaseDigestReceipt,
            SiteGitHubArchiveTokens.UppercaseDigestOutput, SiteGitHubArchiveTokens.UppercaseDigest, null, token);
        await SiteGitHubArchiveRejectionAssertions.AssertInvalidDigestAsync(
            inputs, temporary.Path, SiteGitHubArchiveTokens.ZeroBytesReceipt,
            SiteGitHubArchiveTokens.ZeroBytesOutput, null, SiteGitHubArchiveTokens.Zero, token);
        await SiteGitHubArchiveRejectionAssertions.AssertInvalidDigestAsync(
            inputs, temporary.Path, SiteGitHubArchiveTokens.OversizedBytesReceipt,
            SiteGitHubArchiveTokens.OversizedBytesOutput, null,
            SiteGitHubArchiveTokens.MaximumArchiveBytes + SiteGitHubArchiveTokens.One, token);
    }

    [Test]
    public async Task AC_BC_028_ActualExtractionRejectsShorterAndLongerCentralDirectorySizes()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        await AssertDeclaredSizeMismatch(inputs, temporary.Path,
            SiteGitHubArchiveTokens.ShortDeclaredSizeArchive,
            SiteGitHubArchiveTokens.ShortDeclaredSizeOutput, shorter: true, token: token);
        await AssertDeclaredSizeMismatch(inputs, temporary.Path,
            SiteGitHubArchiveTokens.LongDeclaredSizeArchive,
            SiteGitHubArchiveTokens.LongDeclaredSizeOutput, shorter: false, token: token);
    }

    private static async Task AssertDeclaredSizeMismatch(SiteGitHubArchiveInputs inputs, string root,
        string archiveName, string outputName, bool shorter, CancellationToken token)
    {
        var archivePath = Path.Combine(root, archiveName);
        if (shorter)
        {
            await SiteGitHubArchiveRawZipMutation.CreateShorterDeclaredLengthAsync(
                inputs.ArchivePath, archivePath, token);
        }
        else
        {
            await SiteGitHubArchiveRawZipMutation.CreateLongerDeclaredLengthAsync(
                inputs.ArchivePath, archivePath, token);
        }

        var receiptPath = await SiteGitHubArchiveMutation.CreateMatchingReceiptAsync(inputs.ReceiptPath,
            archivePath, Path.Combine(root, SiteGitHubArchiveTokens.ValidReceiptMutationFile), token);
        await SiteGitHubArchiveRejectionAssertions.AssertRejectedAsync(archivePath, receiptPath,
            Path.Combine(root, outputName), Path.Combine(root, SiteGitHubArchiveTokens.EvidenceName),
            SiteGitHubArchiveTokens.StreamBoundsExceeded, token);
    }

    private static async Task AssertExistingDestinationPreserved(SiteGitHubArchiveInputs inputs, string root,
        CancellationToken token)
    {
        var destination = Path.Combine(root, SiteGitHubArchiveTokens.ExistingReportsDirectory);
        Directory.CreateDirectory(destination);
        var sentinel = Path.Combine(destination, SiteGitHubArchiveTokens.OutputSentinel);
        await File.WriteAllTextAsync(sentinel, SiteGitHubArchiveTokens.OutputSentinelValue, token);
        var evidenceRoot = Path.Combine(root, SiteGitHubArchiveTokens.CoverageDirectory);
        Directory.CreateDirectory(evidenceRoot);
        await SiteGitHubArchiveRejectionAssertions.AssertThrowsMessageAsync<InvalidOperationException>(
            inputs.ArchivePath, inputs.ReceiptPath,
            destination, evidenceRoot, SiteGitHubArchiveTokens.ExistingDestination, token);
        await Assert.That(await File.ReadAllTextAsync(sentinel, token))
            .IsEqualTo(SiteGitHubArchiveTokens.OutputSentinelValue);
    }
}

internal enum SiteGitHubArchiveMutationKind
{
    DuplicateFile,
    UnexpectedFile,
    CaseAlias,
    AbsolutePath,
    BackslashPath,
    TraversalPath,
    SymbolicLink,
    DuplicateDirectory,
    OptionalEmptyDirectory,
    NonemptyDirectory,
    OversizedFile,
    TotalUncompressed,
}

internal sealed record SiteGitHubArchiveMutationCase(SiteGitHubArchiveMutationKind Kind, string FileName,
    string DestinationName, string EvidenceName, string ExpectedError);

internal static class SiteGitHubArchiveMutation
{
    public static readonly SiteGitHubArchiveMutationCase[] PathCases =
    [
        new(SiteGitHubArchiveMutationKind.DuplicateFile, SiteGitHubArchiveTokens.DuplicateArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.DuplicateEntry),
        new(SiteGitHubArchiveMutationKind.UnexpectedFile, SiteGitHubArchiveTokens.UnexpectedArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.InvalidEntry),
        new(SiteGitHubArchiveMutationKind.CaseAlias, SiteGitHubArchiveTokens.CaseAliasArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.DuplicateEntry),
        new(SiteGitHubArchiveMutationKind.AbsolutePath, SiteGitHubArchiveTokens.AbsoluteArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.InvalidEntry),
        new(SiteGitHubArchiveMutationKind.BackslashPath, SiteGitHubArchiveTokens.BackslashArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.InvalidEntry),
        new(SiteGitHubArchiveMutationKind.TraversalPath, SiteGitHubArchiveTokens.TraversalArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.InvalidEntry),
        new(SiteGitHubArchiveMutationKind.SymbolicLink, SiteGitHubArchiveTokens.SymlinkArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.InvalidEntry),
        new(SiteGitHubArchiveMutationKind.DuplicateDirectory, SiteGitHubArchiveTokens.DirectoryArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.DuplicateEntry),
    ];

    public static readonly SiteGitHubArchiveMutationCase[] BoundCases =
    [
        new(SiteGitHubArchiveMutationKind.NonemptyDirectory, SiteGitHubArchiveTokens.DirectoryArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.InvalidDirectory),
        new(SiteGitHubArchiveMutationKind.OversizedFile, SiteGitHubArchiveTokens.FileBoundsArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.EntryTooLarge),
        new(SiteGitHubArchiveMutationKind.TotalUncompressed, SiteGitHubArchiveTokens.TotalBoundsArchiveFile,
            SiteGitHubArchiveTokens.MutationOutputDirectory, SiteGitHubArchiveTokens.CoverageDirectory,
            SiteGitHubArchiveTokens.TotalTooLarge),
    ];

    public static async Task CreateAsync(string sourcePath, string destinationPath,
        SiteGitHubArchiveMutationKind kind, CancellationToken token)
    {
        if (kind == SiteGitHubArchiveMutationKind.TotalUncompressed)
        {
            await CreateWithLargeEntries(sourcePath, destinationPath, token);
            return;
        }

        await using var sourceStream = File.OpenRead(sourcePath);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        await using var destinationStream = File.Create(destinationPath);
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create);
        foreach (var entry in source.Entries)
        {
            if ((kind == SiteGitHubArchiveMutationKind.OptionalEmptyDirectory ||
                 kind == SiteGitHubArchiveMutationKind.NonemptyDirectory) &&
                entry.FullName == SiteGitHubArchiveTokens.OptionalProfileDirectory)
            {
                continue;
            }

            await CopyEntry(entry, destination, kind, token);
            if (kind == SiteGitHubArchiveMutationKind.DuplicateFile &&
                entry.FullName == SiteGitHubArchiveTokens.SmokeProfile + SiteGitHubArchiveTokens.ProfileSeparator +
                SiteGitHubArchiveTokens.ResultsJson)
            {
                await CopyEntry(entry, destination, SiteGitHubArchiveMutationKind.DuplicateFile, token);
            }
        }

        await AddMalformedEntry(destination, kind, token);
    }

    public static async Task<string> CreateMatchingReceiptAsync(string sourceReceipt, string archivePath,
        string receiptPath, CancellationToken token)
    {
        var receipt = JsonNode.Parse(await File.ReadAllBytesAsync(sourceReceipt, token))!.AsObject();
        var archiveNode = receipt[SiteGitHubArchiveTokens.Archive]!.AsObject();
        await using var stream = File.OpenRead(archivePath);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        archiveNode[SiteGitHubArchiveTokens.Sha256] = hash;
        archiveNode[SiteGitHubArchiveTokens.Bytes] = stream.Length;
        await File.WriteAllBytesAsync(receiptPath, JsonSerializer.SerializeToUtf8Bytes(receipt), token);
        return receiptPath;
    }

    private static async Task CopyEntry(ZipArchiveEntry source, ZipArchive destination,
        SiteGitHubArchiveMutationKind kind, CancellationToken token)
    {
        var target = destination.CreateEntry(source.FullName, CompressionLevel.Optimal);
        target.ExternalAttributes = kind == SiteGitHubArchiveMutationKind.SymbolicLink &&
                                    source.FullName == SiteGitHubArchiveTokens.SymlinkEntry
            ? SiteGitHubArchiveTokens.SymlinkMode
            : source.ExternalAttributes;
        await using var sourceContent = await source.OpenAsync(token);
        await using var targetContent = await target.OpenAsync(token);
        if (kind == SiteGitHubArchiveMutationKind.OversizedFile &&
            source.FullName == SiteGitHubArchiveTokens.SmokeProfile + SiteGitHubArchiveTokens.ProfileSeparator +
            SiteGitHubArchiveTokens.ResultsJson)
        {
            await WriteZeros(targetContent, SiteGitHubArchiveTokens.MaximumEntryBytes + SiteGitHubArchiveTokens.One, token);
            return;
        }

        if (kind == SiteGitHubArchiveMutationKind.TotalUncompressed && !source.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal))
        {
            await WriteZeros(targetContent, SiteGitHubArchiveTokens.TotalBoundsEachFileBytes, token);
            return;
        }

        await sourceContent.CopyToAsync(targetContent, token);
    }

    private static async Task AddMalformedEntry(ZipArchive archive, SiteGitHubArchiveMutationKind kind,
        CancellationToken token)
    {
        switch (kind)
        {
            case SiteGitHubArchiveMutationKind.UnexpectedFile:
                await AddEntry(archive, SiteGitHubArchiveTokens.UnexpectedEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                break;
            case SiteGitHubArchiveMutationKind.CaseAlias:
                await AddEntry(archive, SiteGitHubArchiveTokens.CaseAliasEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                break;
            case SiteGitHubArchiveMutationKind.AbsolutePath:
                await AddEntry(archive, SiteGitHubArchiveTokens.AbsoluteEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                break;
            case SiteGitHubArchiveMutationKind.BackslashPath:
                await AddEntry(archive, SiteGitHubArchiveTokens.BackslashEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                break;
            case SiteGitHubArchiveMutationKind.TraversalPath:
                await AddEntry(archive, SiteGitHubArchiveTokens.TraversalEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                break;
            case SiteGitHubArchiveMutationKind.SymbolicLink:
                break;
            case SiteGitHubArchiveMutationKind.DuplicateDirectory:
                await AddEntry(archive, SiteGitHubArchiveTokens.DirectoryEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                await AddEntry(archive, SiteGitHubArchiveTokens.DirectoryEntry, SiteGitHubArchiveTokens.EmptyValue, token);
                break;
            case SiteGitHubArchiveMutationKind.NonemptyDirectory:
                await AddEntry(archive, SiteGitHubArchiveTokens.OptionalProfileDirectory,
                    SiteGitHubArchiveTokens.DirectoryContent, token);
                break;
            case SiteGitHubArchiveMutationKind.OptionalEmptyDirectory:
                await AddEntry(archive, SiteGitHubArchiveTokens.OptionalProfileDirectory,
                    SiteGitHubArchiveTokens.EmptyValue, token);
                break;
        }
    }

    private static async Task AddEntry(ZipArchive archive, string name, string content, CancellationToken token,
        int externalAttributes = SiteGitHubArchiveTokens.Zero)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.ExternalAttributes = externalAttributes;
        await using var stream = await entry.OpenAsync(token);
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(content), token);
    }

    private static async Task CreateWithLargeEntries(string sourcePath, string destinationPath,
        CancellationToken token)
    {
        await using var sourceStream = File.OpenRead(sourcePath);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        await using var destinationStream = File.Create(destinationPath);
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create);
        foreach (var entry in source.Entries)
        {
            await CopyEntry(entry, destination, SiteGitHubArchiveMutationKind.TotalUncompressed, token);
        }
    }

    private static async Task WriteZeros(Stream destination, long bytes, CancellationToken token)
    {
        var zeros = new byte[SiteGitHubArchiveTokens.MutationWriteBufferBytes];
        while (bytes > SiteGitHubArchiveTokens.Zero)
        {
            var count = (int)Math.Min(bytes, zeros.Length);
            await destination.WriteAsync(zeros.AsMemory(SiteGitHubArchiveTokens.Zero, count), token);
            bytes -= count;
        }
    }
}
