using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Reads the actual lifetime peak resident bytes for this diagnostic process.</summary>
internal static partial class ScaledRawStorageProcessMemory
{
    private const string NativeLibraryName = "/usr/lib/libSystem.B.dylib";
    private const string GetResourceUsageEntryPoint = "getrusage";
    private const int CurrentProcessUsage = 0;
    private const int ResourceUsageSizeBytes = 144;
    private const int MaximumResidentSetSizeOffsetBytes = 32;
    private const string NonCurrentProcessMessage = "Peak process memory can only be read for the current process.";
    private const string NonPositivePeakMessage = "The current process did not provide a positive lifetime peak resident byte count.";

    internal static long ReadPeakBytes(Process currentProcess)
    {
        const int PeakBytesValidationBoundary = 0;

        ArgumentNullException.ThrowIfNull(currentProcess);

        if (currentProcess.Id != Environment.ProcessId)
        {
            throw new ArgumentException(NonCurrentProcessMessage, nameof(currentProcess));
        }

        currentProcess.Refresh();

        var peakBytes = OperatingSystem.IsMacOS() && Environment.Is64BitProcess
            ? ReadMacOsPeakBytes()
            : currentProcess.PeakWorkingSet64;

        if (peakBytes <= PeakBytesValidationBoundary)
        {
            throw new InvalidDataException(NonPositivePeakMessage);
        }

        return peakBytes;
    }

    private static long ReadMacOsPeakBytes()
    {
        const int EmptyGetResourceUsage = 0;

        if (GetResourceUsage(CurrentProcessUsage, out var usage) != EmptyGetResourceUsage)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return usage.MaximumResidentSetSizeBytes;
    }

    [LibraryImport(NativeLibraryName, EntryPoint = GetResourceUsageEntryPoint, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int GetResourceUsage(int who, out ResourceUsage usage);

    [StructLayout(LayoutKind.Explicit, Size = ResourceUsageSizeBytes)]
    private struct ResourceUsage
    {
        [FieldOffset(MaximumResidentSetSizeOffsetBytes)]
        internal long MaximumResidentSetSizeBytes;
    }
}
