using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyLoad.Storage.IO;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class OfflineNativeDescriptorDuplicate
{
    private const string Libc = "libc";
    private const string DuplicateEntryPoint = "dup";
    private const int FirstValidDescriptor = 0;

    internal static SafeFileHandle Create(SafeFileHandle original)
    {
        var library = NativeLibrary.Load(Libc, typeof(OfflineNativeDescriptorDuplicate).Assembly,
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
        { NativeLibrary.Free(library); }
        catch (Exception cleanup)
        {
            var failure = primaryFailure is null ? cleanup : new AggregateException(primaryFailure, cleanup);
            OfflineNativeHandleLease.DisposeUntransferred(ref duplicate, failure);
            if (primaryFailure is null)
            { throw; }
            throw failure;
        }
    }

    private static SafeFileHandle Invoke(IntPtr library, SafeFileHandle original)
    {
        var duplicate = Marshal.GetDelegateForFunctionPointer<DuplicateDescriptor>(
            NativeLibrary.GetExport(library, DuplicateEntryPoint));
        var descriptor = duplicate(original.DangerousGetHandle().ToInt32());
        if (descriptor < FirstValidDescriptor)
        { throw new IOException("The native test descriptor could not be duplicated.", new Win32Exception(Marshal.GetLastPInvokeError())); }
        return new(new IntPtr(descriptor), ownsHandle: true);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int DuplicateDescriptor(int descriptor);
}
