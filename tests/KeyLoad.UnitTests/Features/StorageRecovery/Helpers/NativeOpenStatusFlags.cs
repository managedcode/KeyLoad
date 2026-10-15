using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeOpenStatusFlags
{
    private const string Libc = "libc";
    private const string EntryPoint = "fcntl";
    private const string Failed = "The actual native file status flags could not be read.";
    private const int GetStatusFlags = 3;
    private const int Failure = -1;

    internal static int Read(FileStream stream)
    {
        var library = IntPtr.Zero;
        var result = Failure;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            library = NativeLibrary.Load(Libc, typeof(NativeOpenStatusFlags).Assembly,
                DllImportSearchPath.SafeDirectories);
            var read = Marshal.GetDelegateForFunctionPointer<ReadStatusFlags>(
                NativeLibrary.GetExport(library, EntryPoint));
            result = read(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), GetStatusFlags);
            if (result == Failure)
            {
                throw new IOException(Failed, new Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }, failures);
        if (library != IntPtr.Zero)
        {
            ServerFailureObserver.Observe(() => NativeLibrary.Free(library), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int ReadStatusFlags(int descriptor, int command);
}
