namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbePaths
{
    internal static void RequireDirectory(string path)
    {
        const int EmptyInfoAttributesFileAttributesReparsePoint = 0;

        RequirePathAncestors(path);
        var info = new DirectoryInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != EmptyInfoAttributesFileAttributesReparsePoint)
        { throw Invalid(); }
        RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateDirectoryMode);
    }

    private static void RequirePathAncestors(string path)
    {
        const int EmptyInfoAttributesFileAttributesReparsePoint = 0;

        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != RequestCqrsProbeProtocol.FixedRoot)
        { throw Invalid(); }
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != EmptyInfoAttributesFileAttributesReparsePoint)
        { throw Invalid(); }
    }

    internal static void RequirePrivateMode(string path, UnixFileMode expected)
    {
        if (OperatingSystem.IsWindows() || File.GetUnixFileMode(path) != expected)
        { throw Invalid(); }
    }

    internal static string RequireRoot(RequestCqrsProbeOptions options)
    {
        if (!options.Enabled || options.Root != RequestCqrsProbeProtocol.FixedRoot
            || !RequestCqrsProbeOptionsReader.IsSessionId(options.SessionId))
        { throw Invalid(); }
        return options.Root;
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
