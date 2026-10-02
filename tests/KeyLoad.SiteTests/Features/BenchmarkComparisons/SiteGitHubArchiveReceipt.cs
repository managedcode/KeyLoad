using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteGitHubArchiveReceipt(
    int SchemaVersion,
    SiteGitHubArchiveDigest Archive,
    string ReportsRoot,
    SiteGitHubArchiveFileReceipt[] Files)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        WriteIndented = true,
    };

    public static async Task RetainAsync(
        string evidenceRoot,
        SiteGitHubArchiveReceipt receipt,
        CancellationToken cancellationToken)
    {
        var parent = Directory.GetParent(evidenceRoot)?.FullName ??
                     throw new InvalidOperationException(SiteGitHubArchiveTokens.CoverageRootMissing);
        var directory = Path.Combine(parent, SiteGitHubArchiveTokens.ExtractionDirectory);
        Directory.CreateDirectory(directory);
        var receiptPath = Path.Combine(directory, SiteGitHubArchiveTokens.ExtractionReceipt);
        var stagingPath = receiptPath + SiteGitHubArchiveTokens.StageSuffix +
                          Guid.NewGuid().ToString(SiteGitHubArchiveTokens.GuidFormat);
        var ownsStagingFile = false;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
            await using (var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsStagingFile = true;
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(stagingPath, receiptPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or
                                           UnauthorizedAccessException or JsonException or InvalidOperationException or
                                           ArgumentException or SecurityException)
        {
            if (ownsStagingFile && File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }

            throw;
        }
    }
}

internal sealed record SiteGitHubArchiveDigest(string Sha256, long Bytes);

internal sealed record SiteGitHubArchiveFileReceipt(string Path, string Sha256, long Bytes);

internal sealed record SiteGitHubArchiveEntryReceipt(
    string Path,
    string Sha256,
    long Bytes,
    ZipArchiveEntry Entry);

internal sealed record SiteGitHubArchiveVerificationReceipt(
    int SchemaVersion,
    string State,
    string Mode,
    bool PublishEligible,
    SiteGitHubArchiveDigest Archive)
{
    public static async Task<SiteGitHubArchiveVerificationReceipt> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > SiteGitHubArchiveTokens.MaximumReceiptBytes)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            var json = await File.ReadAllBytesAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var result = document.RootElement;
            var receipt = new SiteGitHubArchiveVerificationReceipt(
                result.GetProperty(SiteGitHubArchiveTokens.SchemaVersion).GetInt32(),
                RequiredString(result, SiteGitHubArchiveTokens.State),
                RequiredString(result, SiteGitHubArchiveTokens.Mode),
                result.GetProperty(SiteGitHubArchiveTokens.PublishEligible).GetBoolean(),
                ReadArchive(result.GetProperty(SiteGitHubArchiveTokens.Archive)));
            var modeMatchesEligibility =
                (receipt.Mode == SiteGitHubArchiveTokens.Publish && receipt.PublishEligible) ||
                (receipt.Mode == SiteGitHubArchiveTokens.Validate && !receipt.PublishEligible);
            if (receipt.SchemaVersion != SiteGitHubArchiveTokens.SchemaVersionNumber ||
                receipt.State != SiteGitHubArchiveTokens.ArchiveVerified ||
                !modeMatchesEligibility)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            return receipt;
        }
        catch (Exception exception) when (exception is JsonException or IOException or
                                           InvalidOperationException or KeyNotFoundException or
                                           FormatException or OverflowException)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt, exception);
        }
    }

    private static SiteGitHubArchiveDigest ReadArchive(JsonElement archive)
    {
        var digest = new SiteGitHubArchiveDigest(
            RequiredString(archive, SiteGitHubArchiveTokens.Sha256),
            archive.GetProperty(SiteGitHubArchiveTokens.Bytes).GetInt64());
        if (digest.Sha256.Length != SiteGitHubArchiveTokens.Sha256Length ||
            digest.Bytes <= SiteGitHubArchiveTokens.Zero ||
            digest.Bytes > SiteGitHubArchiveTokens.MaximumArchiveBytes ||
            !digest.Sha256.All(IsLowerHexDigit))
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
        }

        return digest;
    }

    private static bool IsLowerHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ??
        throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
}

internal static class SiteGitHubArchiveFileOperations
{
    public static async Task<SiteGitHubArchiveFileReceipt[]> ExtractEntriesAsync(
        List<SiteGitHubArchiveEntryReceipt> entries,
        string destination,
        CancellationToken cancellationToken)
    {
        var extracted = new List<SiteGitHubArchiveFileReceipt>(entries.Count);
        foreach (var item in entries)
        {
            var target = SafeTargetPath(destination, item.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            extracted.Add(await CopyAndHashAsync(item, target, cancellationToken));
        }

        return extracted.ToArray();
    }

    public static async Task<SiteGitHubArchiveFileReceipt> HashEntryAsync(
        ZipArchiveEntry entry,
        long declaredBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await entry.OpenAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SiteGitHubArchiveTokens.CopyBufferBytes];
        long total = SiteGitHubArchiveTokens.Zero;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > SiteGitHubArchiveTokens.Zero)
        {
            total = checked(total + read);
            if (total > declaredBytes || total > SiteGitHubArchiveTokens.MaximumEntryBytes ||
                total > SiteGitHubArchiveTokens.MaximumUncompressedBytes)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.StreamBoundsExceeded);
            }

            hash.AppendData(buffer, SiteGitHubArchiveTokens.Zero, read);
        }

        if (total != declaredBytes)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.StreamBoundsExceeded);
        }

        return new(entry.FullName, Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    public static async Task<byte[]> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = SiteGitHubArchiveTokens.Zero;
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        stream.Position = SiteGitHubArchiveTokens.Zero;
        return digest;
    }

    public static async Task<SiteGitHubArchiveFileReceipt> HashExtractedFileAsync(
        string root,
        SiteGitHubArchiveFileReceipt expected,
        CancellationToken cancellationToken)
    {
        var path = SafeTargetPath(root, expected.Path);
        var directory = Path.GetDirectoryName(path)!;
        var attributes = File.GetAttributes(path);
        var rootAttributes = File.GetAttributes(root);
        var directoryAttributes = File.GetAttributes(directory);
        var info = new FileInfo(path);
        if (info.Length != expected.Bytes || info.Length > SiteGitHubArchiveTokens.MaximumEntryBytes ||
            (attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0 ||
            (rootAttributes & FileAttributes.ReparsePoint) != 0 ||
            (directoryAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
        }

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SiteGitHubArchiveTokens.CopyBufferBytes];
        long total = SiteGitHubArchiveTokens.Zero;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > SiteGitHubArchiveTokens.Zero)
        {
            total = checked(total + read);
            if (total > expected.Bytes || total > SiteGitHubArchiveTokens.MaximumEntryBytes)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            hash.AppendData(buffer, SiteGitHubArchiveTokens.Zero, read);
        }

        return new(expected.Path, Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    public static void EnsureArchiveIdentity(byte[] digest, long archiveBytes, SiteGitHubArchiveDigest expected)
    {
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(expected.Sha256)) ||
            archiveBytes != expected.Bytes)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
        }
    }

    public static string SafeTargetPath(string root, string entryPath)
    {
        var relative = entryPath.Replace(SiteGitHubArchiveTokens.ProfileSeparator,
            Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidEntry);
        }

        return fullPath;
    }

    private static async Task<SiteGitHubArchiveFileReceipt> CopyAndHashAsync(
        SiteGitHubArchiveEntryReceipt expected,
        string target,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var input = await expected.Entry.OpenAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SiteGitHubArchiveTokens.CopyBufferBytes];
        long total = SiteGitHubArchiveTokens.Zero;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > SiteGitHubArchiveTokens.Zero)
        {
            total = checked(total + read);
            if (total > expected.Bytes || total > SiteGitHubArchiveTokens.MaximumEntryBytes)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.StreamBoundsExceeded);
            }

            hash.AppendData(buffer, SiteGitHubArchiveTokens.Zero, read);
            await output.WriteAsync(buffer.AsMemory(SiteGitHubArchiveTokens.Zero, read), cancellationToken);
        }

        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (total != expected.Bytes || digest != expected.Sha256)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchive);
        }

        return new(expected.Path, digest, total);
    }
}
