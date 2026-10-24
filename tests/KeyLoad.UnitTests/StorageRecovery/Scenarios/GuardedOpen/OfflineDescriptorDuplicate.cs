using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyLoad.Storage.ZoneTree.IO;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class OfflineDescriptorDuplicate
{
    private const string Libc = "libc";
    private const string DuplicateEntryPoint = "dup";

    internal static SafeFileHandle Create(SafeFileHandle original)
    {
        var library = NativeLibrary.Load(Libc, typeof(OfflineDescriptorDuplicate).Assembly,
            DllImportSearchPath.SafeDirectories);
        SafeFileHandle? duplicate = null;
        Exception? primaryFailure = null;
        try
        {
            duplicate = Invoke(library, original);
            return duplicate;
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            ReleaseLibrary(library, ref duplicate, primaryFailure);
        }
    }

    private static void ReleaseLibrary(IntPtr library, ref SafeFileHandle? duplicate, Exception? primaryFailure)
    {
        try
        {
            NativeLibrary.Free(library);
        }
        catch (Exception cleanup)
        {
            var failure = primaryFailure is null ? cleanup : new AggregateException(primaryFailure, cleanup);
            OfflineHandleLease.DisposeUntransferred(ref duplicate, failure);
            if (primaryFailure is null)
            {
                throw;
            }
            throw failure;
        }
    }

    private static SafeFileHandle Invoke(IntPtr library, SafeFileHandle original)
    {
        var duplicate = Marshal.GetDelegateForFunctionPointer<DuplicateDescriptor>(
            NativeLibrary.GetExport(library, DuplicateEntryPoint));
        var descriptor = duplicate(original.DangerousGetHandle().ToInt32());
        if (descriptor < 0)
        {
            throw new IOException("The execution test descriptor could not be duplicated.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        return new(new IntPtr(descriptor), ownsHandle: true);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int DuplicateDescriptor(int descriptor);
}
