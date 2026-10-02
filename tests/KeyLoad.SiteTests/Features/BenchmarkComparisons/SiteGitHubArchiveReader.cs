using System.Buffers.Binary;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteGitHubArchiveReader
{
    public static async Task<SiteGitHubArchiveReceipt> ExtractAsync(
        string archivePath,
        string receiptPath,
        string destination,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        await using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        SiteGitHubArchiveZip.EnsureArchiveBound(archiveStream.Length);
        var initialDigest = await SiteGitHubArchiveFileOperations.HashStreamAsync(archiveStream, cancellationToken);
        var verification = await SiteGitHubArchiveVerificationReceipt.ReadAsync(receiptPath, cancellationToken);
        SiteGitHubArchiveFileOperations.EnsureArchiveIdentity(initialDigest, archiveStream.Length, verification.Archive);
        var staging = destination + SiteGitHubArchiveTokens.StageDirectorySuffix +
                      Guid.NewGuid().ToString(SiteGitHubArchiveTokens.GuidFormat);
        return await ExtractAndRetainAsync(archiveStream, initialDigest, staging, destination, evidenceRoot,
            cancellationToken);
    }

    private static async Task<SiteGitHubArchiveReceipt> ExtractAndRetainAsync(
        FileStream archiveStream,
        byte[] initialDigest,
        string staging,
        string destination,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        var destinationOwned = false;
        try
        {
            var files = await SiteGitHubArchiveZip.ExtractIntoStagingAsync(archiveStream, staging, cancellationToken);
            var finalDigest = await SiteGitHubArchiveFileOperations.HashStreamAsync(archiveStream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(initialDigest, finalDigest))
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.InvalidArchiveReceipt);
            }

            Directory.Move(staging, destination);
            destinationOwned = true;
            var receipt = CreateReceipt(initialDigest, archiveStream.Length, destination, files);
            await SiteGitHubArchiveReceipt.RetainAsync(evidenceRoot, receipt, cancellationToken);
            return receipt;
        }
        catch (Exception exception) when (IsArchiveOperationFailure(exception))
        {
            DeleteOwnedOutput(staging, destination, destinationOwned);
            throw;
        }
    }

    private static SiteGitHubArchiveReceipt CreateReceipt(
        byte[] digest,
        long archiveBytes,
        string destination,
        SiteGitHubArchiveFileReceipt[] files) =>
        new(SiteGitHubArchiveTokens.SchemaVersionNumber,
            new(Convert.ToHexStringLower(digest), archiveBytes), destination, files);

    private static void DeleteOwnedOutput(string staging, string destination, bool destinationOwned)
    {
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        if (destinationOwned)
        {
            Directory.Delete(destination, recursive: true);
        }
    }

    private static bool IsArchiveOperationFailure(Exception exception) =>
        exception is InvalidDataException || exception is IOException || exception is UnauthorizedAccessException ||
        exception is OperationCanceledException || exception is ArgumentException ||
        exception is InvalidOperationException || exception is OverflowException ||
        exception is CryptographicException || exception is NotSupportedException || exception is JsonException ||
        exception is SecurityException;
}

internal static class SiteGitHubArchiveRawZipMutation
{
    public static async Task CreateCorruptArchiveAsync(
        string sourcePath, string destinationPath, CancellationToken token)
    {
        File.Copy(sourcePath, destinationPath);
        await using var stream = new FileStream(destinationPath, FileMode.Open, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(new[] { SiteGitHubArchiveTokens.CorruptHeaderByte }, token);
    }

    public static async Task CreateOversizedArchiveAsync(
        string sourcePath, string destinationPath, CancellationToken token)
    {
        File.Copy(sourcePath, destinationPath);
        await using var stream = new FileStream(destinationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.SetLength(SiteGitHubArchiveTokens.MaximumArchiveBytes + SiteGitHubArchiveTokens.One);
        stream.Position = SiteGitHubArchiveTokens.Zero;
        _ = await SHA256.HashDataAsync(stream, token);
    }

    public static Task CreateShorterDeclaredLengthAsync(
        string sourcePath, string destinationPath, CancellationToken token) =>
        CreateDeclaredLengthMutationAsync(sourcePath, destinationPath, shorter: true, token);

    public static Task CreateLongerDeclaredLengthAsync(
        string sourcePath, string destinationPath, CancellationToken token) =>
        CreateDeclaredLengthMutationAsync(sourcePath, destinationPath, shorter: false, token);

    private static async Task CreateDeclaredLengthMutationAsync(
        string sourcePath, string destinationPath, bool shorter, CancellationToken token)
    {
        await CreateUncompressedCopyAsync(sourcePath, destinationPath, token);
        var archive = await File.ReadAllBytesAsync(destinationPath, token);
        var endRecord = FindEndRecord(archive);
        var centralOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(
            endRecord + SiteGitHubArchiveTokens.EndOfCentralDirectoryCentralDirectoryOffset,
            SiteGitHubArchiveTokens.ZipUInt32Length)));
        var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(
            endRecord + SiteGitHubArchiveTokens.EndOfCentralDirectoryCentralDirectorySizeOffset,
            SiteGitHubArchiveTokens.ZipUInt32Length));
        if ((long)centralOffset + centralSize != endRecord)
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.CentralDirectoryMissing);
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(
            endRecord + SiteGitHubArchiveTokens.EndOfCentralDirectoryEntryCountOffset,
            sizeof(ushort)));
        ChangeDeclaredLength(archive, centralOffset, endRecord, count, shorter);
        await File.WriteAllBytesAsync(destinationPath, archive, token);
    }

    private static async Task CreateUncompressedCopyAsync(
        string sourcePath, string destinationPath, CancellationToken token)
    {
        await using var input = File.OpenRead(sourcePath);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                copy.ExternalAttributes = entry.ExternalAttributes;
                await using var sourceContent = await entry.OpenAsync(token);
                await using var targetContent = await copy.OpenAsync(token);
                await sourceContent.CopyToAsync(targetContent, token);
            }
        }

        await output.FlushAsync(token);
    }

    private static int FindEndRecord(byte[] archive)
    {
        var lowerBound = Math.Max(SiteGitHubArchiveTokens.Zero,
            archive.Length - SiteGitHubArchiveTokens.EndOfCentralDirectoryLength -
            SiteGitHubArchiveTokens.MaximumZipCommentBytes);
        for (var offset = archive.Length - SiteGitHubArchiveTokens.EndOfCentralDirectoryLength;
             offset >= lowerBound; offset--)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(
                offset, SiteGitHubArchiveTokens.EndOfCentralDirectorySignatureLength));
            if (signature != SiteGitHubArchiveTokens.EndOfCentralDirectorySignature)
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(
                offset + SiteGitHubArchiveTokens.EndOfCentralDirectoryCommentLengthOffset, sizeof(ushort)));
            if (offset + SiteGitHubArchiveTokens.EndOfCentralDirectoryLength + commentLength == archive.Length)
            {
                return offset;
            }
        }

        throw new InvalidDataException(SiteGitHubArchiveTokens.CentralDirectoryMissing);
    }

    private static void ChangeDeclaredLength(byte[] archive, int centralOffset, int endRecord,
        ushort count, bool shorter)
    {
        var offset = centralOffset;
        for (var index = SiteGitHubArchiveTokens.Zero; index < count; index++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(
                offset + SiteGitHubArchiveTokens.CentralDirectorySignatureOffset,
                SiteGitHubArchiveTokens.CentralDirectorySignatureLength));
            var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(
                offset + SiteGitHubArchiveTokens.CentralDirectoryFileNameLengthOffset, sizeof(ushort)));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(
                offset + SiteGitHubArchiveTokens.CentralDirectoryExtraLengthOffset, sizeof(ushort)));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(
                offset + SiteGitHubArchiveTokens.CentralDirectoryCommentLengthOffset, sizeof(ushort)));
            var recordLength = checked(SiteGitHubArchiveTokens.CentralDirectoryHeaderLength + fileNameLength +
                                       extraLength + commentLength);
            if (signature != SiteGitHubArchiveTokens.CentralDirectoryHeaderSignature ||
                offset + recordLength > endRecord)
            {
                throw new InvalidDataException(SiteGitHubArchiveTokens.CentralDirectoryMissing);
            }

            var fileName = Encoding.UTF8.GetString(archive, offset + SiteGitHubArchiveTokens.CentralDirectoryHeaderLength,
                fileNameLength);
            if (fileName == SiteGitHubArchiveTokens.CentralDirectoryTargetEntry)
            {
                WriteChangedLength(archive, offset, shorter);
                return;
            }

            offset += recordLength;
        }

        throw new InvalidDataException(SiteGitHubArchiveTokens.CentralDirectoryMissing);
    }

    private static void WriteChangedLength(byte[] archive, int headerOffset, bool shorter)
    {
        var lengthOffset = headerOffset + SiteGitHubArchiveTokens.CentralDirectoryUncompressedSizeOffset;
        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(
            lengthOffset, SiteGitHubArchiveTokens.ZipUInt32Length));
        if (declaredLength == SiteGitHubArchiveTokens.Zero ||
            (!shorter && declaredLength == SiteGitHubArchiveTokens.MaximumZipEntryLength))
        {
            throw new InvalidDataException(SiteGitHubArchiveTokens.CentralDirectoryMissing);
        }

        var changedLength = shorter ? declaredLength - SiteGitHubArchiveTokens.One :
            declaredLength + SiteGitHubArchiveTokens.One;
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(
            lengthOffset, SiteGitHubArchiveTokens.ZipUInt32Length), changedLength);
    }
}
