namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Applies the private control store's bounded name, mode, content and quota rules.</summary>
internal static class RequestCqrsProbeFileValidation
{
    internal const string TemporaryPrefix = "tmp-";
    private const string TemporarySuffix = ".tmp";
    private const string JsonSuffix = ".json";
    internal const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static string[] ValidateContents(string directory)
    {
        ValidateDirectory(directory);
        var entries = new List<string>(RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter + 1);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (entries.Count == RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter)
            { throw new IOException(RequestCqrsProbeFixtureProtocol.FileLimitExceeded); }
            entries.Add(entry);
        }
        long total = 0;
        var validEntries = new List<string>(entries.Count);
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            var temporary = IsTemporaryName(name);
            if (!IsAllowedFileName(name))
            { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
            if (!TryValidateLength(new FileInfo(path), temporary, out var length))
            { continue; }
            if (length > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
            { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
            validEntries.Add(path);
            total = checked(total + length);
        }
        if (total > RequestCqrsProbeFixtureProtocol.MaximumVoterBytes)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.ByteLimitExceeded); }
        return validEntries.ToArray();
    }

    internal static void EnsureQuota(string directory, int additionalBytes)
    {
        var entries = ValidateContents(directory);
        long bytes = additionalBytes;
        foreach (var path in entries)
        {
            if (TryValidateLength(new FileInfo(path), IsTemporaryName(Path.GetFileName(path)), out var length))
            { bytes = checked(bytes + length); }
        }
        if (entries.Length >= RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.FileLimitExceeded); }
        if (bytes > RequestCqrsProbeFixtureProtocol.MaximumVoterBytes)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.ByteLimitExceeded); }
    }

    internal static bool IsAllowedFileName(string name)
    {
        if (name == RequestCqrsProbeFixtureProtocol.OwnerFileName)
        { return true; }
        if (IsTemporaryName(name))
        { return true; }
        if (HasGuidFile(name, RequestCqrsProbeFixtureProtocol.ArmFilePrefix, 1))
        { return true; }
        if (HasGuidFile(name, RequestCqrsProbeFixtureProtocol.ReleaseFilePrefix, 2))
        { return true; }
        return name.StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix, StringComparison.Ordinal)
            && name.EndsWith(JsonSuffix, StringComparison.Ordinal)
            && RequestCqrsProbeFileNames.IsMarkerName(name);
    }

    internal static bool IsTemporaryName(string name)
    {
        if (!name.StartsWith(TemporaryPrefix, StringComparison.Ordinal)
            || !name.EndsWith(TemporarySuffix, StringComparison.Ordinal))
        { return false; }
        var id = name.AsSpan(TemporaryPrefix.Length, name.Length - TemporaryPrefix.Length - TemporarySuffix.Length);
        return Guid.TryParseExact(id, "N", out var temporaryId)
            && temporaryId.ToString("N").AsSpan().SequenceEqual(id);
    }

    private static bool TryValidateLength(FileInfo info, bool temporary, out long length)
    {
        length = 0;
        try
        {
            var identity = KeyLoad.Storage.IO.OfflineRegularFile.Inspect(info.FullName);
            if (File.GetUnixFileMode(info.FullName) != PrivateFileMode)
            { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
            length = identity.Length;
            if (!temporary && length <= 0)
            { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
            return true;
        }
        catch (FileNotFoundException) when (temporary)
        { return false; }
        catch (DirectoryNotFoundException) when (temporary)
        { return false; }
    }

    private static bool HasGuidFile(string name, string prefix, int guidCount)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(JsonSuffix, StringComparison.Ordinal))
        { return false; }
        var parts = name[prefix.Length..^JsonSuffix.Length].Split('-');
        if (parts.Length != guidCount)
        { return false; }
        foreach (var part in parts)
        {
            if (!Guid.TryParseExact(part, "N", out var value) || value.ToString("N") != part)
            { return false; }
        }
        return true;
    }

    internal static void ValidateDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0
            || File.GetUnixFileMode(path) != PrivateDirectoryMode)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
    }

    internal static void EnsureUnixPermissions()
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
    }

    internal static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
