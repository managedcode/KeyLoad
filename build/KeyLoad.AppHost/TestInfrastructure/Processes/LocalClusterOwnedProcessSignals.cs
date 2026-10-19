using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Processes;

/// <summary>Owns execution signaling for one already-owned local CLUSTER child process.</summary>
internal static partial class LocalClusterOwnedProcessSignals
{
    private const string LibraryName = "libc";
    private const string SignalEntryPoint = "kill";
    private const int SignalTerminate = 15;
    private const int CompletionCount = 0;

    internal static void TrySendTerminate(Process process, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(() =>
        {
            if (process.HasExited || OperatingSystem.IsWindows())
            {
                return;
            }
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
            {
                process.Kill(entireProcessTree: true);
            }
        }, failures);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(LibraryName, EntryPoint = SignalEntryPoint, SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
