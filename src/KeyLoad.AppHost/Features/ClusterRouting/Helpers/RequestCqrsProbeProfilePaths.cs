using System.Text.Json;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Validates pre-created private control paths without creating, changing or deleting them.</summary>
internal static class RequestCqrsProbeProfilePaths
{
    private const string CurrentDirectorySegment = ".";
    private const string ParentDirectorySegment = "..";

    private const string OwnerFileName = "owner.json";
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const int MaximumPathCharacters = 4_096;
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static IReadOnlyDictionary<string, string> Validate(string configuredRoot, string sessionId,
        string dataRoot)
    {
        try
        {
            ValidatePlatform();
            var root = CanonicalDirectory(configuredRoot);
            var data = CanonicalDirectory(dataRoot);
            if (IsWithin(root, data) || IsWithin(data, root))
            { throw new InvalidOperationException(InvalidConfiguration); }
            ValidateDirectoryAndAncestors(root, PrivateDirectoryMode);
            ValidateRootEntries(root);
            var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var voter in RequestCqrsProbeProfileSettingsReader.VoterNames)
            {
                var directory = Path.Combine(root, voter);
                ValidateDirectoryAndAncestors(directory, PrivateDirectoryMode);
                ValidateOwnerDirectory(directory, sessionId, global::ClusterResources.Origin(voter));
                nodes.Add(voter, directory);
            }
            return nodes;
        }
        catch (Exception error) when (IsPathOrFileFailure(error))
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static string CanonicalDirectory(string path)
    {
        if (path.Length is 0 or > MaximumPathCharacters || !Path.IsPathFullyQualified(path))
        { throw new InvalidOperationException(InvalidConfiguration); }
        var full = Path.GetFullPath(path);
        var trimmed = Path.TrimEndingDirectorySeparator(full);
        if (!string.Equals(full, path, StringComparison.Ordinal)
            || !string.Equals(trimmed, full, StringComparison.Ordinal)
            || string.Equals(trimmed, Path.GetPathRoot(full), StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidConfiguration); }
        return full;
    }

    private static void ValidatePlatform()
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(InvalidConfiguration); }
    }

    private static void ValidateDirectoryAndAncestors(string path, UnixFileMode? expectedMode)
    {
        if (OperatingSystem.IsWindows())
        { throw new InvalidOperationException(InvalidConfiguration); }
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidOperationException(InvalidConfiguration); }
            if (string.Equals(current, path, StringComparison.Ordinal) && expectedMode is { } mode
                && File.GetUnixFileMode(current) != mode)
            { throw new InvalidOperationException(InvalidConfiguration); }
        }
    }

    private static void ValidateRootEntries(string root)
    {
        var voters = RequestCqrsProbeProfileSettingsReader.VoterNames;
        var entries = Directory.EnumerateFileSystemEntries(root).Take(voters.Count + 1).ToArray();
        if (entries.Length != voters.Count
            || voters.Any(voter => !entries.Contains(Path.Combine(root, voter), StringComparer.Ordinal)))
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static void ValidateOwnerDirectory(string directory, string sessionId, string voter)
    {
        var entries = Directory.EnumerateFileSystemEntries(directory).Take(2).ToArray();
        if (entries.Length != 1 || !string.Equals(Path.GetFileName(entries[0]), OwnerFileName, StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidConfiguration); }
        var ownerPath = Path.Combine(directory, OwnerFileName);
        var owner = RequestCqrsProbeOwnerReader.ReadFile(ownerPath);
        if (!string.Equals(owner.SessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(owner.Voter, voter, StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == CurrentDirectorySegment || (!Path.IsPathRooted(relative) && relative != ParentDirectorySegment
            && !relative.StartsWith(ParentDirectorySegment + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(ParentDirectorySegment + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static bool IsPathOrFileFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException or JsonException or global::KeyLoad.KeyLoadException;
}
