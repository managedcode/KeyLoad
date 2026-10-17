namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Applies the private control store's bounded name, mode, content and quota rules.</summary>
internal static class RequestCqrsProbeFileValidation
{
    internal const string TemporaryPrefix = "tmp-";
    internal const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static string[] ValidateContents(string directory)
    {
        ValidateDirectory(directory);
        var entries = new List<string>(RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter + 1);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (entries.Count == RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter)
            {
                throw new IOException(RequestCqrsProbeFixtureProtocol.FileLimitExceeded);
            }
            entries.Add(entry);
        }
        long total = 0;
        var validEntries = new List<string>(entries.Count);
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            var temporary = IsTemporaryName(name);
            if (!IsAllowedFileName(name))
            {
                throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
            }
            if (!TryValidateLength(new FileInfo(path), temporary, out var length))
            {
                continue;
            }
            if (length > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
            {
                throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
            }
            validEntries.Add(path);
            total = checked(total + length);
        }
        if (total > RequestCqrsProbeFixtureProtocol.MaximumVoterBytes)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.ByteLimitExceeded);
        }
        return validEntries.ToArray();
    }

    internal static void EnsureQuota(string directory, int additionalBytes)
    {
        var entries = ValidateContents(directory);
        long bytes = additionalBytes;
        foreach (var path in entries)
        {
            if (TryValidateLength(new FileInfo(path), IsTemporaryName(Path.GetFileName(path)), out var length))
            {
                bytes = checked(bytes + length);
            }
        }
        if (entries.Length >= RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.FileLimitExceeded);
        }
        if (bytes > RequestCqrsProbeFixtureProtocol.MaximumVoterBytes)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.ByteLimitExceeded);
        }
    }

    internal static bool IsAllowedFileName(string name)
        => RequestCqrsProbeNameValidation.IsAllowedFileName(name);

    internal static bool IsTemporaryName(string name)
        => RequestCqrsProbeNameValidation.IsTemporaryName(name);

    private static bool TryValidateLength(FileInfo info, bool temporary, out long length)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        length = 0;
        try
        {
            var identity = KeyLoad.Storage.ZoneTree.IO.OfflineRegularFile.Inspect(info.FullName);
            if (File.GetUnixFileMode(info.FullName) != PrivateFileMode)
            {
                throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
            }
            length = identity.Length;
            if (!temporary && length <= 0)
            {
                throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
            }
            return true;
        }
        catch (FileNotFoundException) when (temporary)
        {
            return false;
        }
        catch (DirectoryNotFoundException) when (temporary)
        {
            return false;
        }
    }

    internal static void ValidateDirectory(string path)
        => RequestCqrsProbeDirectoryValidation.ValidateDirectory(path);

    internal static void EnsureUnixPermissions()
        => RequestCqrsProbeDirectoryValidation.EnsureUnixPermissions();

    internal static bool IsWithin(string candidate, string root)
        => RequestCqrsProbeDirectoryValidation.IsWithin(candidate, root);
}
