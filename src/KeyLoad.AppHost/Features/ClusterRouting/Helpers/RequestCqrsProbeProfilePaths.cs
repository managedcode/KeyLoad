using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Validates pre-created private control paths without creating, changing or deleting them.</summary>
internal static class RequestCqrsProbeProfilePaths
{
    private const string CurrentDirectorySegment = ".";
    private const string ParentDirectorySegment = "..";

    private const string OwnerFileName = "owner.json";
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static IReadOnlyDictionary<string, string> Validate(string configuredRoot, string sessionId,
        string dataRoot, IOptions<RequestProbeFileOptions> executionOptions)
        => Validate(configuredRoot, sessionId, dataRoot, executionOptions,
            RequestCqrsProbeProfileSettingsReader.VoterNames, RequestCqrsProbeOwnerReader.OwnerVersion,
            RequestCqrsProbeOwnerReader.OwnerKind);

    internal static IReadOnlyDictionary<string, string> Validate(string configuredRoot, string sessionId,
        string dataRoot, IOptions<RequestProbeFileOptions> executionOptions, IReadOnlyList<string> voters,
        int ownerVersion, string ownerKind)
    {
        try
        {
            ValidatePlatform();
            var root = CanonicalDirectory(configuredRoot, executionOptions);
            var data = CanonicalDirectory(dataRoot, executionOptions);
            if (IsWithin(root, data) || IsWithin(data, root))
            { throw new InvalidOperationException(InvalidConfiguration); }
            ValidateDirectoryAndAncestors(root, PrivateDirectoryMode);
            ValidateRootEntries(root, voters);
            var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var voter in voters)
            {
                var directory = Path.Combine(root, voter);
                ValidateDirectoryAndAncestors(directory, PrivateDirectoryMode);
                ValidateOwnerDirectory(directory, sessionId, global::ClusterResources.Origin(voter), executionOptions, ownerVersion, ownerKind);
                nodes.Add(voter, directory);
            }
            return nodes;
        }
        catch (Exception error) when (IsPathOrFileFailure(error))
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static string CanonicalDirectory(string path, IOptions<RequestProbeFileOptions> executionOptions)
    {
        const int StructuralValue = 0;

        if (path.Length == StructuralValue || path.Length > executionOptions.Value.MaximumPathCharacters || !Path.IsPathFullyQualified(path))
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
        const int OwnerEntryCount = 0;

        if (OperatingSystem.IsWindows())
        { throw new InvalidOperationException(InvalidConfiguration); }
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == OwnerEntryCount || (attributes & FileAttributes.ReparsePoint) != OwnerEntryCount)
            { throw new InvalidOperationException(InvalidConfiguration); }
            if (string.Equals(current, path, StringComparison.Ordinal) && expectedMode is { } mode
                && File.GetUnixFileMode(current) != mode)
            { throw new InvalidOperationException(InvalidConfiguration); }
        }
    }

    private static void ValidateRootEntries(string root, IReadOnlyList<string> voters)
    {
        const int Step = 1;

        var entries = Directory.EnumerateFileSystemEntries(root).Take(voters.Count + Step).ToArray();
        if (entries.Length != voters.Count
            || voters.Any(voter => !entries.Contains(Path.Combine(root, voter), StringComparer.Ordinal)))
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static void ValidateOwnerDirectory(string directory, string sessionId, string voter, IOptions<RequestProbeFileOptions> executionOptions, int ownerVersion, string ownerKind)
    {
        const int CountValue = 2;
        const int OwnerEntryCount = 1;
        const int FirstIndex = 0;

        var entries = Directory.EnumerateFileSystemEntries(directory).Take(CountValue).ToArray();
        if (entries.Length != OwnerEntryCount || !string.Equals(Path.GetFileName(entries[FirstIndex]), OwnerFileName, StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidConfiguration); }
        var ownerPath = Path.Combine(directory, OwnerFileName);
        var owner = RequestCqrsProbeOwnerReader.ReadFile(ownerPath, executionOptions, ownerVersion, ownerKind);
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
