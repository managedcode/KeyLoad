namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class TestDatabaseDeleteFailurePermission
{
    private readonly string root;
    private readonly UnixFileMode original;
    private bool restoreRequired;

    internal TestDatabaseDeleteFailurePermission(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(TestDatabaseDeleteFailureProtocol.UnixRequired);
        }
        this.root = root;
        original = File.GetUnixFileMode(root);
    }

    internal void DenyAndRequireEnumerationRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(TestDatabaseDeleteFailureProtocol.UnixRequired);
        }
        restoreRequired = true;
        File.SetUnixFileMode(root, original & ~TestDatabaseDeleteFailureProtocol.ReadWriteBits);
        try
        {
            _ = Directory.EnumerateFileSystemEntries(root).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.EnumerationMustRefuse);
    }

    internal void Restore()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(TestDatabaseDeleteFailureProtocol.UnixRequired);
        }
        if (restoreRequired)
        {
            File.SetUnixFileMode(root, original);
            restoreRequired = false;
        }
    }

    internal static void RequireUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(TestDatabaseDeleteFailureProtocol.UnixRequired);
        }
    }
}
