using System.Runtime.InteropServices;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static partial class RequestCqrsProbeAppHostNativeFiles
{
    private const uint PrivateFifoMode = 0x180;
    private const string Libc = "libc";

    internal static void CreateFifo(string path)
    {
        if (CreateFifoNative(path, PrivateFifoMode) != 0)
        { throw new IOException("Could not create the test-owned FIFO.", Marshal.GetLastPInvokeError()); }
    }

    [LibraryImport(Libc, EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int CreateFifoNative(string path, uint mode);
}
