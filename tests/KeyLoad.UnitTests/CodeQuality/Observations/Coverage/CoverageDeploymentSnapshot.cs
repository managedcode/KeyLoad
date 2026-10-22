using System.Security.Cryptography;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed class CoverageDeploymentSnapshot
{
    private const int PermissionMask = 0x1ff;
    private const int UnboundedPathCharacters = int.MaxValue;
    private const int InitialBufferOffset = 0;
    private const int NoBytesRead = 0;
    private const int NoBytesRemaining = 0;
    private const int EndOfFile = -1;
    private const string SourceChangedDuringCaptureMessage =
        "A execution coverage source file changed while its identity was captured.";
    private const string SourceFileBoundExceededMessage =
        "A execution coverage source file exceeds its configured file bound.";
    private const string SourceClosureBoundExceededMessage =
        "A execution coverage source closure exceeds its configured total-byte bound.";
    private const string DeploymentCopyMismatchMessage =
        "A execution coverage deployment copy differs from its captured source closure.";
    private const string SourceClosureChangedMessage =
        "A execution coverage source closure changed after its original snapshot.";
    private const string SourceFileBoundChangedMessage =
        "A execution coverage source file changed beyond its configured bound.";

    private sealed record FileIdentity(long Length, int Mode, string Sha256);
    private readonly IReadOnlyDictionary<string, FileIdentity> files;

    private CoverageDeploymentSnapshot(IReadOnlyDictionary<string, FileIdentity> files) => this.files = files;

    internal static CoverageDeploymentSnapshot Capture(string root, int maximumFiles, int maximumFileBytes,
        long maximumTotalBytes, int readBufferBytes)
        => Capture(root, maximumFiles, maximumFileBytes, maximumTotalBytes, null, readBufferBytes);

    internal static CoverageDeploymentSnapshot Capture(string root, int maximumFiles, int maximumFileBytes,
        long maximumTotalBytes, int maximumPathCharacters, int readBufferBytes)
        => Capture(root, maximumFiles, maximumFileBytes, maximumTotalBytes, (int?)maximumPathCharacters, readBufferBytes);

    private static CoverageDeploymentSnapshot Capture(string root, int maximumFiles, int maximumFileBytes,
        long maximumTotalBytes, int? maximumPathCharacters, int readBufferBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readBufferBytes);
        var identities = new Dictionary<string, FileIdentity>(StringComparer.Ordinal);
        var buffer = new byte[readBufferBytes];
        long totalBytes = 0;
        var paths = maximumPathCharacters is { } pathLimit
            ? CoverageDeploymentClosure.EnumerateFiles(root, maximumFiles, pathLimit)
            : CoverageDeploymentClosure.EnumerateFiles(root, maximumFiles, UnboundedPathCharacters);
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 0 || info.Length > maximumFileBytes)
            {
                throw new InvalidDataException(SourceFileBoundExceededMessage);
            }
            totalBytes = checked(totalBytes + info.Length);
            if (totalBytes > maximumTotalBytes)
            {
                throw new InvalidDataException(SourceClosureBoundExceededMessage);
            }
            var mode = ReadMode(path);
            var hash = HashExact(path, info.Length, maximumFileBytes, buffer);
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != info.Length || after.LastWriteTimeUtc != info.LastWriteTimeUtc
                || after.LinkTarget is not null || (after.Attributes & FileAttributes.ReparsePoint) != 0
                || ReadMode(path) != mode)
            {
                throw new InvalidDataException(SourceChangedDuringCaptureMessage);
            }
            identities.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                new(info.Length, mode, hash));
        }
        return new(identities);
    }

    internal void VerifyUnchanged(string root, int maximumFiles, int maximumFileBytes, long maximumTotalBytes,
        int readBufferBytes)
    {
        VerifyUnchanged(root, maximumFiles, maximumFileBytes, maximumTotalBytes, null, readBufferBytes);
    }

    internal void VerifyUnchanged(string root, int maximumFiles, int maximumFileBytes,
        long maximumTotalBytes, int maximumPathCharacters, int readBufferBytes)
        => VerifyUnchanged(root, maximumFiles, maximumFileBytes, maximumTotalBytes,
            (int?)maximumPathCharacters, readBufferBytes);

    internal void VerifyCopyMatches(string root, int maximumFiles, int maximumFileBytes,
        long maximumTotalBytes, int maximumPathCharacters, int readBufferBytes)
    {
        var current = Capture(root, maximumFiles, maximumFileBytes, maximumTotalBytes,
            maximumPathCharacters, readBufferBytes).files;
        if (!Matches(current))
        {
            throw new InvalidDataException(DeploymentCopyMismatchMessage);
        }
    }

    private void VerifyUnchanged(string root, int maximumFiles, int maximumFileBytes,
        long maximumTotalBytes, int? maximumPathCharacters, int readBufferBytes)
    {
        var current = Capture(root, maximumFiles, maximumFileBytes, maximumTotalBytes,
            maximumPathCharacters, readBufferBytes).files;
        if (!Matches(current))
        {
            throw new InvalidDataException(SourceClosureChangedMessage);
        }
    }

    private static string HashExact(string path, long expectedLength, int maximumFileBytes, byte[] buffer)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            buffer.Length, FileOptions.SequentialScan);
        if (stream.Length != expectedLength || stream.Length > maximumFileBytes)
        {
            throw new InvalidDataException(SourceFileBoundChangedMessage);
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var remaining = expectedLength;
        while (remaining > NoBytesRemaining)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, InitialBufferOffset, requested);
            if (read == NoBytesRead)
            {
                throw new InvalidDataException(SourceChangedDuringCaptureMessage);
            }
            digest.AppendData(buffer, InitialBufferOffset, read);
            remaining -= read;
        }
        if (stream.ReadByte() != EndOfFile)
        {
            throw new InvalidDataException(SourceChangedDuringCaptureMessage);
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static int ReadMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution coverage deployment permissions require a Unix filesystem.");
        }
        return (int)File.GetUnixFileMode(path) & PermissionMask;
    }

    private bool Matches(IReadOnlyDictionary<string, FileIdentity> current)
        => files.Count == current.Count && files.All(entry =>
            current.TryGetValue(entry.Key, out var identity) && identity == entry.Value);
}
