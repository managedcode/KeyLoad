using System.Security.Cryptography;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

/// <summary>Captures and revalidates the canonical native collector settings used by owned CLI operations.</summary>
internal sealed record NativeCoverageSettingsSnapshot(string RepositoryRoot, string Path, long Length, string Sha256)
{
    private const string SettingsRelativePath = "scripts/Features/CodeQuality/functional-coverage.production.settings.xml";
    private const string InvalidSettingsMessage = "The canonical native coverage settings changed or exceeded their bound.";
    private const string ParentDirectoryMarker = "..";
    private const int MinimumSettingsBytes = 1;
    private const int InitialBufferIndex = 0;
    private const long InitialObservedLength = 0;

    internal static NativeCoverageSettingsSnapshot Capture(string repositoryRoot, NativeCoverageExecutionOptions options)
    {
        var root = System.IO.Path.GetFullPath(repositoryRoot);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, SettingsRelativePath));
        var identity = ReadStableFile(root, path, options);
        return new(root, path, identity.Length, identity.Sha256);
    }

    internal void VerifyUnchanged(NativeCoverageExecutionOptions options)
    {
        var identity = ReadStableFile(RepositoryRoot, Path, options);
        if (identity.Length != Length || identity.Sha256 != Sha256)
        {
            throw new InvalidDataException(InvalidSettingsMessage);
        }
    }

    private static (long Length, string Sha256) ReadStableFile(string root, string path,
        NativeCoverageExecutionOptions options)
    {
        ValidatePath(root, path, options.MaximumPathCharacters);
        var before = new FileInfo(path);
        if (!before.Exists || before.Length < MinimumSettingsBytes || before.Length > options.MaximumFileBytes
            || IsReparsePoint(before))
        {
            throw new InvalidDataException(InvalidSettingsMessage);
        }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            options.ReadBufferBytes, FileOptions.SequentialScan);
        if (input.Length != before.Length)
        {
            throw new InvalidDataException(InvalidSettingsMessage);
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[options.ReadBufferBytes];
        var length = InitialObservedLength;
        int read;
        while ((read = input.Read(buffer, InitialBufferIndex, buffer.Length)) > InitialBufferIndex)
        {
            if (length > options.MaximumFileBytes - read)
            {
                throw new InvalidDataException(InvalidSettingsMessage);
            }
            length += read;
            digest.AppendData(buffer, InitialBufferIndex, read);
        }
        var after = new FileInfo(path);
        if (length != before.Length || input.Length != before.Length || !after.Exists
            || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc
            || IsReparsePoint(after))
        {
            throw new InvalidDataException(InvalidSettingsMessage);
        }
        return (length, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    private static void ValidatePath(string root, string path, int maximumPathCharacters)
    {
        if (!System.IO.Path.IsPathFullyQualified(root) || !System.IO.Path.IsPathFullyQualified(path)
            || path.Length > maximumPathCharacters)
        {
            throw new InvalidDataException(InvalidSettingsMessage);
        }
        var relative = System.IO.Path.GetRelativePath(root, path);
        var parentPrefix = ParentDirectoryMarker + System.IO.Path.DirectorySeparatorChar;
        if (System.IO.Path.IsPathFullyQualified(relative) || relative == ParentDirectoryMarker
            || relative.StartsWith(parentPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(InvalidSettingsMessage);
        }
        DirectoryInfo? current = new(System.IO.Path.GetDirectoryName(path)!);
        while (current is not null)
        {
            if (IsReparsePoint(current))
            {
                throw new InvalidDataException(InvalidSettingsMessage);
            }
            if (string.Equals(current.FullName, root, StringComparison.Ordinal))
            {
                return;
            }
            current = current.Parent;
        }
        throw new InvalidDataException(InvalidSettingsMessage);
    }

    private static bool IsReparsePoint(FileSystemInfo entry)
        => entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0;
}
