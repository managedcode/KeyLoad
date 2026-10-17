namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeDirectoryValidation
{
    internal static void ValidateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0
            || File.GetUnixFileMode(path) != RequestCqrsProbeFileValidation.PrivateDirectoryMode)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
        }
    }

    internal static void EnsureUnixPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
    }

    internal static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

}
