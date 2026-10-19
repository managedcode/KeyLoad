using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Reads selected Windows process and parent identifiers from one execution Toolhelp snapshot.</summary>
internal static partial class ProjectProcessParentWindows
{
    private const uint ProcessSnapshotFlag = 0x00000002;
    private const uint IgnoredProcessId = 0;
    private const int NoMoreFiles = 18;
    private const int InitialOutputCharacters = 0;
    private const int OutputRowSeparatorCharacters = 1;
    private const string LibraryName = "kernel32.dll";
    private const string SnapshotEntryPoint = "CreateToolhelp32Snapshot";
    private const string FirstEntryPoint = "Process32FirstW";
    private const string NextEntryPoint = "Process32NextW";

    internal static unsafe int[] Read(int processId, bool children, IOptions<TestExecutionOptions> options,
        CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(options.Value.ProcessSettlementTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        deadline.Token.ThrowIfCancellationRequested();
        using var snapshot = CreateSnapshot(ProcessSnapshotFlag, IgnoredProcessId);
        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
        var matches = new List<int>();
        var characters = InitialOutputCharacters;
        var available = ReadFirst(snapshot, ref entry);
        while (available)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (children ? entry.ParentProcessId == (uint)processId : entry.ProcessId == (uint)processId)
            {
                var selected = checked((int)(children ? entry.ProcessId : entry.ParentProcessId));
                characters += selected.ToString(CultureInfo.InvariantCulture).Length + OutputRowSeparatorCharacters;
                if (characters > options.Value.CleanupOutputCharacters)
                {
                    throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
                }
                matches.Add(selected);
                if (!children)
                {
                    return [.. matches];
                }
            }
            available = ReadNext(snapshot, ref entry);
        }
        if (Marshal.GetLastPInvokeError() != NoMoreFiles)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        deadline.Token.ThrowIfCancellationRequested();
        return [.. matches];
    }

    [LibraryImport(LibraryName, EntryPoint = SnapshotEntryPoint, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial SafeFileHandle CreateSnapshot(uint flags, uint processId);

    [LibraryImport(LibraryName, EntryPoint = FirstEntryPoint, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFirst(SafeFileHandle snapshot, ref ProcessEntry entry);

    [LibraryImport(LibraryName, EntryPoint = NextEntryPoint, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadNext(SafeFileHandle snapshot, ref ProcessEntry entry);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ProcessEntry
    {
        internal const int MaximumPathCharacters = 260;

        internal uint Size;
        internal uint UsageCount;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint ThreadCount;
        internal uint ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        internal fixed char ExecutableFile[MaximumPathCharacters];
    }
}
