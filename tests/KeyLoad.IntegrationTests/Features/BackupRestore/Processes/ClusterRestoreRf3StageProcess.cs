using System.Diagnostics;
using System.Globalization;
using System.Text;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Stops only the original paused AppHost executable; AppHost retains its readers and graph ownership.</summary>
internal static class ClusterRestoreRf3StageProcess
{
    internal static async Task KillAndJoinAsync(int pid, string repository, Guid operationId,
        NativeClusterRestoreStage stage, int maximumBytes, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || pid < ClusterRestoreRf3ResumeProtocol.MinimumPid)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var failures = new List<Exception>();
        var process = Process.GetProcessById(pid);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var originalStart = process.StartTime;
            RequireOriginal(process, repository, operationId, stage, maximumBytes, originalStart);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (!process.HasExited)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(process.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void RequireOriginal(Process process, string repository, Guid operationId,
        NativeClusterRestoreStage stage, int maximumBytes, DateTime originalStart)
    {
        if (process.HasExited || process.StartTime != originalStart)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var path = Path.Combine(ClusterRestoreRf3ResumeProtocol.ProcRoot,
            process.Id.ToString(CultureInfo.InvariantCulture), ClusterRestoreRf3ResumeProtocol.CommandLine);
        // procfs reports length zero: read into the admitted fixed buffer, never ReadAllBytes.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[checked(maximumBytes + ClusterRestoreRf3ResumeProtocol.OverflowByte)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read > maximumBytes)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var arguments = Encoding.UTF8.GetString(buffer.AsSpan(0, read))
            .Split(ClusterRestoreRf3ResumeProtocol.ArgumentSeparator, StringSplitOptions.RemoveEmptyEntries);
        var assembly = Path.Combine(repository, ClusterRestoreRf3ResumeProtocol.CrashHostDll);
        if (arguments.Length != ClusterRestoreRf3ResumeProtocol.ProcessArguments
            || !string.Equals(Path.GetFileName(arguments[ClusterRestoreRf3ResumeProtocol.ExecutableArgument]), ClusterRestoreRf3ResumeProtocol.Runtime, StringComparison.Ordinal)
            || !string.Equals(arguments[ClusterRestoreRf3ResumeProtocol.AssemblyArgument], assembly, StringComparison.Ordinal)
            || !string.Equals(arguments[ClusterRestoreRf3ResumeProtocol.ModeArgument], ClusterRestoreRf3ResumeProtocol.CutMode, StringComparison.Ordinal)
            || !string.Equals(arguments[ClusterRestoreRf3ResumeProtocol.StageArgument], stage.ToString(), StringComparison.Ordinal)
            || !string.Equals(arguments[ClusterRestoreRf3ResumeProtocol.OperationArgument], operationId.ToString(ClusterRestoreRf3Protocol.IdentityFormat), StringComparison.Ordinal)
            || process.HasExited || process.StartTime != originalStart)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
    }
}
