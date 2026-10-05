using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static partial class RequestCqrsProbeNativeFifo
{
    private const uint PrivateFifoMode = 0x180;
    private const int CreateFailure = -1;

    internal static void Create(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        { throw new PlatformNotSupportedException("Private FIFO tests require the supported Unix test host."); }
        if (MakeFifo(path, PrivateFifoMode) == CreateFailure)
        { throw new IOException("The owned private FIFO fixture could not be created.", new Win32Exception(Marshal.GetLastPInvokeError())); }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "mkfifo", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MakeFifo(string path, uint mode);
}
