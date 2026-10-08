using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses one original operation deadline and one cleanup deadline for all four original children.</summary>
internal static class ControlledPartitionMovementTerminalProcessChildren
{
    private const string Dotnet = "dotnet";
    private static readonly string[] Modes = [ControlledPartitionMovementProcessProtocol.Prepare,
        ControlledPartitionMovementProcessProtocol.Fault, ControlledPartitionMovementProcessProtocol.Recover,
        ControlledPartitionMovementProcessProtocol.Verify];

    internal static async Task RunAsync(string root, NativeMovementProcessOptions options,
        TimeProvider clock, CancellationToken token)
    {
        options.Validate();
        using var cleanup = new ControlledPartitionMovementCleanupDeadline(options, clock);
        foreach (var mode in Modes)
        { await RunChildAsync(root, mode, options, cleanup, token); }
    }

    private static async Task RunChildAsync(string root, string mode, NativeMovementProcessOptions options,
        ControlledPartitionMovementCleanupDeadline cleanup, CancellationToken token)
    {
        var start = new ProcessStartInfo(Dotnet)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, mode })
        { start.ArgumentList.Add(argument); }
        var signal = mode == ControlledPartitionMovementProcessProtocol.Prepare ? CrashFixtureValues.Acknowledgement
            : mode == ControlledPartitionMovementProcessProtocol.Fault ? CrashFixtureValues.CrashMarker : null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var child = new ControlledPartitionMovementTerminalProcessChild(options, signal);
            await RunOwnedChildAsync(child, start, root, cleanup, failures, token);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedChildAsync(ControlledPartitionMovementTerminalProcessChild child,
        ProcessStartInfo start, string root, ControlledPartitionMovementCleanupDeadline cleanup,
        List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            child.Start(start);
            await child.CompleteAsync(token);
        }, failures);
        if (!child.Settled)
        {
            await ServerFailureObserver.ObserveAsync(() => child.JoinCleanupAsync(failures, cleanup.Token), failures);
        }
        if (!child.Settled)
        { ControlledPartitionMovementRetainedChild.Retain(child, root, failures); }
    }
}
