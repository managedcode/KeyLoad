using System.Security.Cryptography;
using KeyLoad.AppHost.Features.CodeQuality;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeEvidenceCopier
{
    internal static async Task CopyAndAttachAsync(NativeCoverageMergeEvidenceInventory.FileEntry entry,
        string destinationRoot, ITestOutput output, NativeCoverageExecutionOptions options)
    {
        var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.RelativePath));
        EnsureContained(destinationRoot, destination, options.MaximumPathCharacters);
        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("A native coverage evidence destination is invalid.");
        Directory.CreateDirectory(destinationDirectory);
        RejectLinkedDirectoryChain(destinationRoot, destinationDirectory);
        await CopyExactAsync(entry, destination, options).ConfigureAwait(false);
        output.AttachArtifact(destination, entry.RelativePath, "Original native coverage tooling evidence");
    }

    private static async Task CopyExactAsync(NativeCoverageMergeEvidenceInventory.FileEntry entry,
        string destination, NativeCoverageExecutionOptions options)
    {
        var before = new FileInfo(entry.SourcePath);
        if (!before.Exists || before.LinkTarget is not null || before.Length != entry.Length
            || before.LastWriteTimeUtc != entry.LastWriteTimeUtc)
        {
            throw new InvalidDataException("A native coverage evidence input changed before retention.");
        }
        byte[] sourceDigest;
        long copied;
        await using (var input = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            options.ReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await using var retained = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                options.ReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[options.ReadBufferBytes];
            copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                if (copied > entry.Length - read || copied > options.MaximumFileBytes - read)
                {
                    throw new InvalidDataException("A native coverage evidence input exceeded its copy bound.");
                }
                copied += read;
                digest.AppendData(buffer, 0, read);
                await retained.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None).ConfigureAwait(false);
            }
            await retained.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            sourceDigest = digest.GetHashAndReset();
            VerifyInput(entry, before, input, copied);
        }
        var retainedDigest = HashRetainedFile(destination, entry.Length, options);
        if (!CryptographicOperations.FixedTimeEquals(sourceDigest, retainedDigest))
        {
            throw new InvalidDataException("A native coverage evidence copy changed its original bytes.");
        }
    }

    private static void VerifyInput(NativeCoverageMergeEvidenceInventory.FileEntry entry, FileInfo before,
        FileStream input, long copied)
    {
        var after = new FileInfo(entry.SourcePath);
        if (copied != entry.Length || input.Length != entry.Length
            || before.LastWriteTimeUtc != after.LastWriteTimeUtc || before.Length != after.Length)
        {
            throw new InvalidDataException("A native coverage evidence copy did not preserve its original length and identity.");
        }
    }

    private static byte[] HashRetainedFile(string path, long expectedLength, NativeCoverageExecutionOptions options)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.LinkTarget is not null || before.Length != expectedLength)
        {
            throw new InvalidDataException("A retained native coverage file has an invalid identity.");
        }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            options.ReadBufferBytes, FileOptions.SequentialScan);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[options.ReadBufferBytes];
        long length = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (length > options.MaximumFileBytes - read)
            {
                throw new InvalidDataException("A retained native coverage file exceeded its bound.");
            }
            length += read;
            digest.AppendData(buffer, 0, read);
        }
        var after = new FileInfo(path);
        return length == expectedLength && input.Length == expectedLength
            && after.Length == expectedLength && after.LastWriteTimeUtc == before.LastWriteTimeUtc
            ? digest.GetHashAndReset()
            : throw new InvalidDataException("A retained native coverage file was truncated or changed.");
    }

    private static void EnsureContained(string root, string path, int maximumPathCharacters)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(fullRoot, StringComparison.Ordinal) || path.Length > maximumPathCharacters)
        {
            throw new InvalidDataException("A native coverage evidence destination escaped its results directory.");
        }
    }

    private static void RejectLinkedDirectoryChain(string root, string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null && current.FullName.Length >= root.Length)
        {
            if (current.LinkTarget is not null || (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("A native coverage evidence destination contains a linked directory.");
            }
            if (string.Equals(current.FullName, root, StringComparison.Ordinal))
            {
                return;
            }
            current = current.Parent;
        }
        throw new InvalidDataException("A native coverage evidence destination is outside its results directory.");
    }
}
