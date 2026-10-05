namespace KeyLoad.AppHost.Features.ClusterReplication;

internal static class ClusterProfilePermissions
{
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectory = PrivateFile | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static UnixFileMode? NewProfileMode
        => OperatingSystem.IsWindows() ? null : PrivateFile;

    internal static void RequirePrivate(string path)
    {
        if (OperatingSystem.IsWindows())
        { return; }
        var mode = File.GetUnixFileMode(path);
        if ((mode & ~OwnerPermissions) != 0 || (mode & UnixFileMode.UserRead) == 0)
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
    }

    internal static void PreparePrivateDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(directory, PrivateDirectory); }
    }

    internal static UnixFileMode? ReadMode(string path)
        => OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);

    internal static void ApplyFileMode(string path, UnixFileMode? mode)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, mode ?? PrivateFile); }
    }

    internal static void EnsureMode(string path, UnixFileMode? expected)
    {
        if (!OperatingSystem.IsWindows() && expected is { } mode && File.GetUnixFileMode(path) != mode)
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
    }
}
