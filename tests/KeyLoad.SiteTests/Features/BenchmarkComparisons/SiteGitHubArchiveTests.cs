using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteGitHubArchiveTests
{
    [Test]
    public async Task AC_BC_028_AuthenticArchiveExtractsTwelveInputsAndRetainsEveryHash()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await AssertAuthenticatedReceipt(inputs.ReceiptPath, token);
        await using var temporary = SiteTempDirectory.Create();
        var evidenceRoot = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.CoverageDirectory);
        Directory.CreateDirectory(evidenceRoot);
        var extraction = await SiteGitHubArchiveSetup.PrepareAsync(inputs.ArchivePath, inputs.ReceiptPath,
            temporary.Output, evidenceRoot, token);

        await Assert.That(extraction.SchemaVersion).IsEqualTo(SiteGitHubArchiveTokens.SchemaVersionNumber);
        await Assert.That(extraction.ReportsRoot).IsEqualTo(temporary.Output);
        await Assert.That(extraction.Files.Length).IsEqualTo(SiteGitHubArchiveTokens.ExpectedArchiveFileCount);
        await AssertArchiveIdentity(inputs.ArchivePath, extraction, token);
        await AssertAllArchiveEntries(inputs.ArchivePath, extraction, token);
        await AssertExtractedInputs(inputs.ArchivePath, temporary.Output, token);
        await AssertPersistedReceipt(evidenceRoot, extraction, token);
    }

    [Test]
    public async Task AC_BC_028_ControlledValidModeEnvelopesAcceptAuthenticatedArchiveDigest()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var original = await SiteGitHubArchiveVerificationReceipt.ReadAsync(inputs.ReceiptPath, token);
        var cases = new[]
        {
            (Mode: SiteGitHubArchiveTokens.Validate, Eligible: false,
                Receipt: SiteGitHubArchiveTokens.ValidateReceipt),
            (Mode: SiteGitHubArchiveTokens.Publish, Eligible: true,
                Receipt: SiteGitHubArchiveTokens.PublishEligibleReceipt),
        };
        foreach (var item in cases)
        {
            await AssertValidModeEnvelope(inputs.ReceiptPath, temporary.Path, original, item, token);
        }
    }

    private static async Task AssertAuthenticatedReceipt(string receiptPath, CancellationToken token)
    {
        var rawBytes = await File.ReadAllBytesAsync(receiptPath, token);
        using var document = JsonDocument.Parse(rawBytes);
        var raw = document.RootElement;
        var expectedSchema = raw.GetProperty(SiteGitHubArchiveTokens.SchemaVersion).GetInt32();
        var expectedState = raw.GetProperty(SiteGitHubArchiveTokens.State).GetString();
        var expectedMode = raw.GetProperty(SiteGitHubArchiveTokens.Mode).GetString();
        var expectedEligibility = raw.GetProperty(SiteGitHubArchiveTokens.PublishEligible).GetBoolean();
        var rawArchive = raw.GetProperty(SiteGitHubArchiveTokens.Archive);
        var expectedHash = rawArchive.GetProperty(SiteGitHubArchiveTokens.Sha256).GetString();
        var expectedBytes = rawArchive.GetProperty(SiteGitHubArchiveTokens.Bytes).GetInt64();
        var supportedPair = (expectedMode == SiteGitHubArchiveTokens.Validate && !expectedEligibility) ||
                            (expectedMode == SiteGitHubArchiveTokens.Publish && expectedEligibility);
        var receipt = await SiteGitHubArchiveVerificationReceipt.ReadAsync(receiptPath, token);
        await Assert.That(supportedPair).IsTrue();
        await Assert.That(expectedSchema).IsEqualTo(SiteGitHubArchiveTokens.SchemaVersionNumber);
        await Assert.That(expectedState).IsEqualTo(SiteGitHubArchiveTokens.ArchiveVerified);
        await Assert.That(expectedHash).IsNotNull();
        await Assert.That(expectedBytes > SiteGitHubArchiveTokens.Zero &&
                          expectedBytes <= SiteGitHubArchiveTokens.MaximumArchiveBytes).IsTrue();
        var expectedDigest = expectedHash!;
        await Assert.That(expectedDigest.Length).IsEqualTo(SiteGitHubArchiveTokens.Sha256Length);
        await Assert.That(expectedDigest.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f'))
            .IsTrue();
        await Assert.That(receipt.SchemaVersion).IsEqualTo(expectedSchema);
        await Assert.That(receipt.State).IsEqualTo(expectedState);
        await Assert.That(receipt.Mode).IsEqualTo(expectedMode);
        await Assert.That(receipt.PublishEligible).IsEqualTo(expectedEligibility);
        await Assert.That(receipt.Archive.Sha256).IsEqualTo(expectedHash);
        await Assert.That(receipt.Archive.Bytes).IsEqualTo(expectedBytes);
    }

    private static async Task AssertValidModeEnvelope(string sourceReceipt, string root,
        SiteGitHubArchiveVerificationReceipt original,
        (string Mode, bool Eligible, string Receipt) item, CancellationToken token)
    {
        var path = Path.Combine(root, item.Receipt);
        await SiteGitHubArchiveVerificationReceiptMutation.CreateAsync(sourceReceipt, path,
            item.Mode, item.Eligible, null, null, token);
        var accepted = await SiteGitHubArchiveVerificationReceipt.ReadAsync(path, token);
        await Assert.That(accepted.Mode).IsEqualTo(item.Mode);
        await Assert.That(accepted.PublishEligible).IsEqualTo(item.Eligible);
        await Assert.That(accepted.Archive.Sha256).IsEqualTo(original.Archive.Sha256);
        await Assert.That(accepted.Archive.Bytes).IsEqualTo(original.Archive.Bytes);
    }

    private static async Task AssertArchiveIdentity(string archivePath, SiteGitHubArchiveReceipt extraction,
        CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(archivePath, token);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await Assert.That(extraction.Archive.Sha256).IsEqualTo(hash);
        await Assert.That(extraction.Archive.Bytes).IsEqualTo(bytes.LongLength);
    }

    private static async Task AssertAllArchiveEntries(string archivePath, SiteGitHubArchiveReceipt receipt,
        CancellationToken token)
    {
        await using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        await Assert.That(archive.Entries.Count(entry => !entry.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal))).IsEqualTo(
                SiteGitHubArchiveTokens.ExpectedArchiveFileCount);
        foreach (var entry in archive.Entries.Where(item => !item.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal)))
        {
            await AssertArchiveEntry(entry, receipt, token);
        }
    }

    private static async Task AssertArchiveEntry(ZipArchiveEntry entry, SiteGitHubArchiveReceipt receipt,
        CancellationToken token)
    {
        var file = receipt.Files.Single(candidate => candidate.Path == entry.FullName);
        await using var content = await entry.OpenAsync(token);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SiteGitHubArchiveTokens.CopyBufferBytes];
        long total = SiteGitHubArchiveTokens.Zero;
        int read;
        while ((read = await content.ReadAsync(buffer, token)) > SiteGitHubArchiveTokens.Zero)
        {
            digest.AppendData(buffer, SiteGitHubArchiveTokens.Zero, read);
            total += read;
        }

        await Assert.That(file.Bytes).IsEqualTo(total);
        await Assert.That(file.Sha256).IsEqualTo(Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    private static async Task AssertExtractedInputs(string archivePath, string reportsRoot, CancellationToken token)
    {
        await using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var inputEntries = archive.Entries.Where(item => !item.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal)).ToArray();
        foreach (var entry in inputEntries)
        {
            var output = Path.Combine(reportsRoot, entry.FullName.Replace(
                SiteGitHubArchiveTokens.ProfileSeparator, Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));
            var source = await ReadEntryAsync(entry, token);
            var extracted = await File.ReadAllBytesAsync(output, token);
            await Assert.That(extracted.SequenceEqual(source)).IsTrue();
        }

        await Assert.That(inputEntries.Length).IsEqualTo(SiteGitHubArchiveTokens.ExpectedArchiveFileCount);
        await Assert.That(Directory.EnumerateFiles(reportsRoot, SiteGitHubArchiveTokens.RunnerLog,
            SearchOption.AllDirectories).Count()).IsEqualTo(SiteGitHubArchiveTokens.ExpectedProfileCount);
        await Assert.That(Directory.EnumerateFiles(reportsRoot, SiteGitHubArchiveTokens.ResultsJson,
            SearchOption.AllDirectories).Count()).IsEqualTo(SiteGitHubArchiveTokens.ExpectedProfileCount);
        await Assert.That(Directory.EnumerateFiles(reportsRoot, SiteGitHubArchiveTokens.SamplesCsv,
            SearchOption.AllDirectories).Count()).IsEqualTo(SiteGitHubArchiveTokens.ExpectedProfileCount);
        await Assert.That(Directory.EnumerateFiles(reportsRoot, SiteGitHubArchiveTokens.ResultsMarkdown,
            SearchOption.AllDirectories).Count()).IsEqualTo(SiteGitHubArchiveTokens.ExpectedProfileCount);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken token)
    {
        await using var content = await entry.OpenAsync(token);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, token);
        return buffer.ToArray();
    }

    private static async Task AssertPersistedReceipt(string evidenceRoot,
        SiteGitHubArchiveReceipt expected, CancellationToken token)
    {
        var parent = Directory.GetParent(evidenceRoot)!.FullName;
        var receiptPath = Path.Combine(parent, SiteGitHubArchiveTokens.ExtractionDirectory,
            SiteGitHubArchiveTokens.ExtractionReceipt);
        var bytes = await File.ReadAllBytesAsync(receiptPath, token);
        var persisted = JsonSerializer.Deserialize<SiteGitHubArchiveReceipt>(bytes,
            SiteGitHubArchiveReceipt.JsonOptions);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted?.SchemaVersion).IsEqualTo(expected.SchemaVersion);
        await Assert.That(persisted?.Archive.Sha256).IsEqualTo(expected.Archive.Sha256);
        await Assert.That(persisted?.Archive.Bytes).IsEqualTo(expected.Archive.Bytes);
        await Assert.That(persisted?.ReportsRoot).IsEqualTo(expected.ReportsRoot);
        await Assert.That(persisted?.Files.Select(file => file.Path)
            .SequenceEqual(expected.Files.Select(file => file.Path), StringComparer.Ordinal)).IsTrue();
        foreach (var file in expected.Files)
        {
            var retained = persisted!.Files.Single(candidate => candidate.Path == file.Path);
            await Assert.That(retained.Sha256).IsEqualTo(file.Sha256);
            await Assert.That(retained.Bytes).IsEqualTo(file.Bytes);
        }
    }
}

internal sealed record SiteGitHubArchiveInputs(string ArchivePath, string ReceiptPath)
{
    public static SiteGitHubArchiveInputs Read()
    {
        var archivePath = RequiredPath(SiteGitHubArchiveTokens.ArchiveEnvironment);
        var receiptPath = RequiredPath(SiteGitHubArchiveTokens.ArchiveReceiptEnvironment);
        if (!File.Exists(archivePath) || !File.Exists(receiptPath))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.ArchiveInputsMissing);
        }

        return new(archivePath, receiptPath);
    }

    private static string RequiredPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.ArchiveInputsMissing);
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(SiteGitHubArchiveTokens.AbsolutePathRequired);
        }

        return path;
    }
}

internal static class SiteGitHubArchiveRejectionAssertions
{
    public static async Task AssertInvalidModeAsync(SiteGitHubArchiveInputs inputs, string root,
        string mode, bool publishEligible, string receiptName, string outputName, CancellationToken token)
    {
        var receipt = await SiteGitHubArchiveVerificationReceiptMutation.CreateAsync(
            inputs.ReceiptPath, Path.Combine(root, receiptName), mode, publishEligible, null, null, token);
        await AssertRejectedAsync(inputs.ArchivePath, receipt, Path.Combine(root, outputName),
            Path.Combine(root, SiteGitHubArchiveTokens.EvidenceName),
            SiteGitHubArchiveTokens.InvalidArchiveReceipt, token);
    }

    public static async Task AssertInvalidDigestAsync(SiteGitHubArchiveInputs inputs, string root,
        string receiptName, string outputName, string? sha256, long? bytes, CancellationToken token)
    {
        var receipt = await SiteGitHubArchiveVerificationReceiptMutation.CreateAsync(
            inputs.ReceiptPath, Path.Combine(root, receiptName), SiteGitHubArchiveTokens.Validate,
            false, sha256, bytes, token);
        await AssertRejectedAsync(inputs.ArchivePath, receipt, Path.Combine(root, outputName),
            Path.Combine(root, SiteGitHubArchiveTokens.EvidenceName),
            SiteGitHubArchiveTokens.InvalidArchiveReceipt, token);
    }

    public static async Task AssertRejectedAsync(string archive, string receipt,
        string destination, string evidenceRoot, string expectedMessage, CancellationToken token)
    {
        Directory.CreateDirectory(evidenceRoot);
        await AssertThrowsMessageAsync<InvalidDataException>(archive, receipt, destination, evidenceRoot,
            expectedMessage, token);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        var stagingPattern = Path.GetFileName(destination) + SiteGitHubArchiveTokens.StageDirectorySuffix +
                             SiteGitHubArchiveTokens.WildcardSuffix;
        var staging = Directory.EnumerateDirectories(Path.GetDirectoryName(destination)!, stagingPattern).Any();
        await Assert.That(staging).IsFalse();
        var retainedReceipt = Path.Combine(Directory.GetParent(evidenceRoot)!.FullName,
            SiteGitHubArchiveTokens.ExtractionDirectory, SiteGitHubArchiveTokens.ExtractionReceipt);
        await Assert.That(File.Exists(retainedReceipt)).IsFalse();
    }

    public static async Task AssertThrowsMessageAsync<TException>(string archive, string receipt,
        string destination, string evidenceRoot, string expectedMessage, CancellationToken token)
        where TException : Exception
    {
        var error = await Assert.ThrowsExactlyAsync<TException>(async () =>
            await SiteGitHubArchiveSetup.PrepareAsync(archive, receipt, destination, evidenceRoot, token));
        await Assert.That(error?.Message).IsEqualTo(expectedMessage);
    }
}

internal static class SiteGitHubArchiveVerificationReceiptMutation
{
    public static async Task<string> CreateAsync(string sourceReceipt, string receiptPath,
        string mode, bool publishEligible, string? sha256, long? bytes, CancellationToken token)
    {
        var receipt = JsonNode.Parse(await File.ReadAllBytesAsync(sourceReceipt, token))!.AsObject();
        receipt[SiteGitHubArchiveTokens.Mode] = mode;
        receipt[SiteGitHubArchiveTokens.PublishEligible] = publishEligible;
        if (sha256 is not null)
        {
            receipt[SiteGitHubArchiveTokens.Archive]![SiteGitHubArchiveTokens.Sha256] = sha256;
        }

        if (bytes.HasValue)
        {
            receipt[SiteGitHubArchiveTokens.Archive]![SiteGitHubArchiveTokens.Bytes] = bytes.Value;
        }

        await File.WriteAllBytesAsync(receiptPath, JsonSerializer.SerializeToUtf8Bytes(receipt), token);
        return receiptPath;
    }
}

internal sealed class SiteGitHubArchiveOptionalDirectoryTests
{
    [Test]
    public async Task AC_BC_028_OptionalEmptyProfileDirectoryPreservesTwelveAuthenticatedPayloads()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var archive = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.OptionalDirectoryArchiveFile);
        await SiteGitHubArchiveMutation.CreateAsync(inputs.ArchivePath, archive,
            SiteGitHubArchiveMutationKind.OptionalEmptyDirectory, token);
        var receipt = await SiteGitHubArchiveMutation.CreateMatchingReceiptAsync(inputs.ReceiptPath, archive,
            Path.Combine(temporary.Path, SiteGitHubArchiveTokens.OptionalDirectoryReceipt), token);
        var evidenceRoot = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.EvidenceName);
        Directory.CreateDirectory(evidenceRoot);
        var extraction = await SiteGitHubArchiveSetup.PrepareAsync(archive, receipt, temporary.Output,
            evidenceRoot, token);

        await Assert.That(extraction.Files.Length).IsEqualTo(SiteGitHubArchiveTokens.ExpectedArchiveFileCount);
        await AssertThatPayloadsMatch(inputs.ArchivePath, archive, extraction.ReportsRoot,
            extraction.Files, token);
    }

    private static async Task AssertThatPayloadsMatch(string originalPath, string copiedPath, string reportsRoot,
        SiteGitHubArchiveFileReceipt[] extractedFiles, CancellationToken token)
    {
        await using var originalStream = File.OpenRead(originalPath);
        await using var copiedStream = File.OpenRead(copiedPath);
        using var original = new ZipArchive(originalStream, ZipArchiveMode.Read);
        using var copied = new ZipArchive(copiedStream, ZipArchiveMode.Read);
        var originalFiles = original.Entries.Where(entry => !entry.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal)).ToArray();
        var copiedFiles = copied.Entries.Where(entry => !entry.FullName.EndsWith(
            SiteGitHubArchiveTokens.DirectorySuffix, StringComparison.Ordinal)).ToArray();
        await Assert.That(originalFiles.Select(entry => entry.FullName)
            .SequenceEqual(copiedFiles.Select(entry => entry.FullName), StringComparer.Ordinal)).IsTrue();
        await Assert.That(copiedFiles.Length).IsEqualTo(SiteGitHubArchiveTokens.ExpectedArchiveFileCount);
        var optionalDirectories = copied.Entries.Where(entry =>
            entry.FullName == SiteGitHubArchiveTokens.OptionalProfileDirectory).ToArray();
        await Assert.That(optionalDirectories.Length).IsEqualTo(SiteGitHubArchiveTokens.One);
        await Assert.That((await ReadEntryAsync(optionalDirectories[SiteGitHubArchiveTokens.Zero], token)).Length)
            .IsEqualTo(SiteGitHubArchiveTokens.Zero);
        foreach (var source in originalFiles)
        {
            var target = copied.GetEntry(source.FullName)!;
            var sourceBytes = await ReadEntryAsync(source, token);
            var targetBytes = await ReadEntryAsync(target, token);
            await Assert.That(targetBytes.SequenceEqual(sourceBytes)).IsTrue();
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
            await Assert.That(Convert.ToHexStringLower(SHA256.HashData(targetBytes))).IsEqualTo(expectedHash);
            var extracted = extractedFiles.Single(file => file.Path == source.FullName);
            await Assert.That(extracted.Bytes).IsEqualTo(sourceBytes.LongLength);
            await Assert.That(extracted.Sha256).IsEqualTo(expectedHash);
            var output = await File.ReadAllBytesAsync(
                SiteGitHubArchiveFileOperations.SafeTargetPath(reportsRoot, source.FullName), token);
            await Assert.That(output.SequenceEqual(targetBytes)).IsTrue();
        }
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken token)
    {
        await using var content = await entry.OpenAsync(token);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, token);
        return buffer.ToArray();
    }
}

internal sealed class SiteGitHubArchiveIntegrityTests
{
    [Test]
    public async Task AC_BC_028_VerifyUnchangedRejectsEditedExtractedInput()
    {
        var inputs = SiteGitHubArchiveInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var evidenceRoot = Path.Combine(temporary.Path, SiteGitHubArchiveTokens.EvidenceName);
        Directory.CreateDirectory(evidenceRoot);
        var extraction = await SiteGitHubArchiveSetup.PrepareAsync(inputs.ArchivePath, inputs.ReceiptPath,
            temporary.Output, evidenceRoot, token);
        await SiteGitHubArchiveSetup.VerifyUnchangedAsync(extraction, token);

        var report = extraction.Files.First(file => file.Bytes > SiteGitHubArchiveTokens.Zero);
        var path = SiteGitHubArchiveFileOperations.SafeTargetPath(extraction.ReportsRoot, report.Path);
        var bytes = await File.ReadAllBytesAsync(path, token);
        bytes[SiteGitHubArchiveTokens.Zero] ^= SiteGitHubArchiveTokens.PayloadMutationBitMask;
        await File.WriteAllBytesAsync(path, bytes, token);
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SiteGitHubArchiveSetup.VerifyUnchangedAsync(extraction, token));
        await Assert.That(error?.Message).IsEqualTo(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
    }
}
