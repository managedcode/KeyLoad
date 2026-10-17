using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceBoundsNativeFixture
{
    internal const string ShellProcess = "/bin/sh";
    internal const string HealthyProcess = "/usr/bin/printf";
    internal const string HealthyOutput = "scale-native-bounds-healthy";
    internal const string OutputBoundMessage = "Server resource sample exceeded its bound.";
    internal const string ErrorBoundMessage = "Server resource diagnostic exceeded its bound.";
    private const string ShellCommandArgument = "-c";
    private const string ProbeArgument = "owned-scale-pipe-probe";
    private const string StartGateCommand = "printf '%s\\nready' \"$$\" > \"$1\"; while [ ! -f \"$2\" ]; do sleep 0.05; done; exec /usr/bin/yes";
    private const string DiagnosticRedirection = " 1>&2";
    private const string OwnedDirectoryChangedMessage = "The owned native pipe probe directory changed before cleanup.";
    private const FileAttributes NoAttributes = (FileAttributes)0;

    internal static string[] Arguments(string marker, string gate, bool diagnostic)
        => [ShellCommandArgument, StartGateCommand + (diagnostic ? DiagnosticRedirection : string.Empty), ProbeArgument, marker, gate];

    internal static async Task StopChildAsync(ScaleServerResourceChildIdentity? child)
    {
        if (child is null)
        {
            return;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(child.Value.ProcessId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == child.Value.StartTimeUtcTicks)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    internal static void DeleteOwnedDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.Directory) == NoAttributes || (attributes & FileAttributes.ReparsePoint) != NoAttributes)
        {
            throw new IOException(OwnedDirectoryChangedMessage);
        }

        Directory.Delete(directory, recursive: true);
    }
}
