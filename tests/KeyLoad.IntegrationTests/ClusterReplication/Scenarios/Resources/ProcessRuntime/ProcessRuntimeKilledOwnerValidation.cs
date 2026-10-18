using System.Diagnostics;
using Aspire.Hosting;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ProcessRuntimeKilledOwnerValidation
{
    internal static IReadOnlyDictionary<string, ProcessRuntimeKillReceipt> Require(
        DistributedApplication owner, DistributedApplication actualOriginal, IReadOnlyCollection<string> names,
        IReadOnlyDictionary<string, ProcessRuntimeKillReceipt> originalReceipts)
    {
        if (!ReferenceEquals(owner, actualOriginal) || names.Count != originalReceipts.Count
            || names.Any(name => !originalReceipts.ContainsKey(name)))
        {
            throw new InvalidOperationException(ProcessRestartOwnership.PendingFailure);
        }
        foreach (var name in names)
        {
            var receipt = originalReceipts[name];
            var before = ProcessRuntimeProtocol.Identity(receipt.Before);
            var killed = receipt.Killed;
            if (receipt.ResourceName != name || killed.ResourceName != name || killed.ProcessId != before.ProcessId
                || killed.AspireStartTimeUtcTicks != before.AspireStartTimeUtcTicks
                || killed.ProcessStartTimeUtcTicks != before.ProcessStartTimeUtcTicks || IsOriginalAlive(before.ProcessId,
                    before.ProcessStartTimeUtcTicks) || killed.LaunchProcessId != before.LaunchProcessId
                || killed.LaunchProcessStartTimeUtcTicks != before.LaunchProcessStartTimeUtcTicks
                || IsOriginalAlive(before.LaunchProcessId, before.LaunchProcessStartTimeUtcTicks)
                || killed.LaunchExitCode == 0
                || (OperatingSystem.IsWindows() ? killed.ExitCode is null or 0 : killed.ExitCode is not null))
            {
                throw new InvalidOperationException(ProcessRestartOwnership.PendingFailure);
            }
        }
        return originalReceipts;
    }

    internal static void RequireExited(NativeProcessEvidence original)
    {
        if (IsOriginalAlive(original.ProcessId, original.StartedAtUtc.Ticks)
            || original.AspireProcessId is not { } launch || original.AspireProcessStartedAtUtc is not { } started
            || IsOriginalAlive(launch, started.Ticks))
        {
            throw new InvalidOperationException("The original native process has not joined its shutdown.");
        }
    }

    private static bool IsOriginalAlive(int processId, long startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startedAt;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
