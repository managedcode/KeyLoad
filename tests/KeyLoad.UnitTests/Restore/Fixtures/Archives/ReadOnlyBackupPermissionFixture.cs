using KeyLoad.Server;

namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class ReadOnlyBackupPermissionFixture : IDisposable
{
    private const string Unsupported = "Read-only backup verification requires Unix file permissions.";
    private readonly Dictionary<string, UnixFileMode> modes = new(StringComparer.Ordinal);
    private readonly HashSet<string> directories;

    internal ReadOnlyBackupPermissionFixture(string backup)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(Unsupported);
        }
        var parent = Path.GetDirectoryName(backup) ?? throw new DirectoryNotFoundException();
        directories = new([parent, backup], StringComparer.Ordinal);
        foreach (var path in directories.Concat(Directory.EnumerateFiles(backup)))
        {
            modes.Add(path, File.GetUnixFileMode(path));
        }
    }

    internal void Protect()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(Unsupported);
        }
        foreach (var path in modes.Keys)
        {
            File.SetUnixFileMode(path, directories.Contains(path)
                ? UnixFileMode.UserRead | UnixFileMode.UserExecute
                : UnixFileMode.UserRead);
        }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var pair in modes)
        {
            ServerFailureObserver.Observe(() =>
            {
                if (OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException(Unsupported);
                }
                File.SetUnixFileMode(pair.Key, pair.Value);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
