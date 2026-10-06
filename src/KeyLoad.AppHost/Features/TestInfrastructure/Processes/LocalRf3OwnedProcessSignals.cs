using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Processes;

/// <summary>Owns native signaling for one already-owned local RF3 child process.</summary>
internal static partial class LocalRf3OwnedProcessSignals
{
    private const string NativeLibraryName = "libc";
    private const string NativeSignalEntryPoint = "kill";
    private const int SignalTerminate = 15;
    private const int CompletionCount = 0;

    internal static void TrySendTerminate(Process process, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(() =>
        {
            if (process.HasExited || OperatingSystem.IsWindows())
            { return; }
            if (SendSignal(process.Id, SignalTerminate) != CompletionCount && !process.HasExited)
            {
                failures.Add(new Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }, failures);
    }

    internal static void TryKill(Process process, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(() =>
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }, failures);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibraryName, EntryPoint = NativeSignalEntryPoint, SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
