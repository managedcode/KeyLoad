using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Owns four original movement child processes and both original readers under existing process deadlines.</summary>
internal static class ControlledPartitionMovementProcessTrial
{
    private const string Dotnet = "dotnet";
    private const string FailedChild = "The original movement child exited unsuccessfully.";
    private const string Unsettled = "The original movement child or readers did not settle.";
    private const string RetainedRoot = "KeyLoad.ControlledMovement.RetainedRoot";
    private const int SuccessExit = 0;
    private static readonly string[] Modes = [ControlledPartitionMovementProcessProtocol.Prepare,
        ControlledPartitionMovementProcessProtocol.Fault, ControlledPartitionMovementProcessProtocol.Recover,
        ControlledPartitionMovementProcessProtocol.Verify];

    internal static async Task RunAsync(string actualOwnerRoot,
        Func<string, CancellationToken, Task> verifyFullOriginalEvidence,
        IOptions<NativeProcessReadinessOptions> readiness, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            foreach (var mode in Modes)
            {
                var killed = mode is ControlledPartitionMovementProcessProtocol.Prepare
                    or ControlledPartitionMovementProcessProtocol.Fault;
                active = CreateChild(mode);
                active.Start(CreateStart(actualOwnerRoot, mode));
                active.ThrowStartupFailure();
                await CompleteAsync(active, killed, cancellationToken);
                await ControlledPartitionMovementProcessOwnership.RequireAsync(actualOwnerRoot, readiness, clock, cancellationToken);
                active.CloseNativeProcessAfterJoin();
                active = null;
                await verifyFullOriginalEvidence(mode, cancellationToken);
            }
        }
        catch (Exception error) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        catch (Exception error) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        await SettleAsync(actualOwnerRoot, active, primary, readiness, clock);
    }

    private static CommandIdempotencyProcessChild CreateChild(string mode)
        => CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
            mode == ControlledPartitionMovementProcessProtocol.Prepare ? CrashFixtureValues.Acknowledgement
            : mode == ControlledPartitionMovementProcessProtocol.Fault ? CrashFixtureValues.CrashMarker : null);

    private static ProcessStartInfo CreateStart(string root, string mode)
    {
        var start = new ProcessStartInfo(Dotnet)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, mode })
        { start.ArgumentList.Add(argument); }
        return start;
    }

    private static async Task CompleteAsync(CommandIdempotencyProcessChild actual, bool killed, CancellationToken token)
    {
        if (killed)
        {
            await actual.WaitForAcknowledgementAsync(token);
            await actual.StopAndJoinAsync(token);
            return;
        }
        await actual.WaitAndJoinAsync(token);
        if (actual.ExitCode != SuccessExit)
        { throw new InvalidOperationException(FailedChild); }
    }

    private static async Task SettleAsync(string root, CommandIdempotencyProcessChild? active, Exception? primary,
        IOptions<NativeProcessReadinessOptions> readiness, TimeProvider clock)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), clock);
        var failures = new List<Exception>();
        if (active is not null)
        { await CommandIdempotencyProcess.SettleActiveAsync(active, failures, cleanup.Token); }
        if (active is { IsSettled: false })
        { failures.Add(new IOException(Unsettled)); }
        if (active is null or { IsSettled: true })
        {
            await ServerFailureObserver.ObserveAsync(
            () => ControlledPartitionMovementProcessOwnership.RequireAsync(root, readiness, clock, cleanup.Token), failures);
        }
        primary?.Data[RetainedRoot] = root;
        foreach (var failure in failures)
        { failure.Data[RetainedRoot] = root; }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}
