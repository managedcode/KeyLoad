using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessTrial
{
    private const string Dotnet = "dotnet";
    private const int SuccessExit = 0;
    private const string Failed = "The genuine native movement child did not settle successfully.";

    internal static async Task RunAsync(string root, CancellationToken cancellationToken)
    {
        foreach (var mode in new[] { ControlledPartitionMovementNativeProcessProtocol.Hold,
            ControlledPartitionMovementNativeProcessProtocol.Observe })
        { await ChildAsync(root, mode, cancellationToken); }
    }

    private static async Task ChildAsync(string root, string mode, CancellationToken cancellationToken)
    {
        var hold = mode == ControlledPartitionMovementNativeProcessProtocol.Hold;
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
            hold ? CrashFixtureValues.Acknowledgement : null);
        var start = new ProcessStartInfo(Dotnet)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, mode })
        { start.ArgumentList.Add(argument); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            child.EnableNativeOutputWitness(hold);
            child.Start(start);
            child.ThrowStartupFailure();
            if (hold)
            {
                await child.WaitForAcknowledgementAsync(cancellationToken);
                await child.StopAndJoinAsync(cancellationToken);
            }
            else
            {
                await child.WaitAndJoinAsync(cancellationToken);
                if (child.ExitCode != SuccessExit)
                { throw new InvalidOperationException(Failed); }
            }
        }, failures);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds));
        await CommandIdempotencyProcess.SettleActiveAsync(child, failures, cleanup.Token);
        if (!child.IsSettled)
        { failures.Add(new IOException(Failed)); }
        await ServerFailureObserver.ObserveAsync(() => child.ReportNativeOutputWitnessAsync(failures), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
