using System.Runtime.InteropServices;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static partial class RequestCqrsProbeAppHostNativeFiles
{
    private const uint PrivateFifoMode = 0x180;
    private const string Libc = "libc";

    internal static void CreateFifo(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        { throw new PlatformNotSupportedException("Private FIFO tests require a supported Unix host."); }
        if (CreateFifoNative(path, PrivateFifoMode) != 0)
        { throw new IOException("Could not create the test-owned FIFO.", Marshal.GetLastPInvokeError()); }
    }

    internal static UnixFileMode GetMode(string path)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private mode tests require a Unix host."); }
        return File.GetUnixFileMode(path);
    }

    internal static void SetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private mode tests require a Unix host."); }
        File.SetUnixFileMode(path, mode);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Libc, EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int CreateFifoNative(string path, uint mode);
}
