using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.AnalyticsStore;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using P = KeyLoad.CrashHost.Features.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.RecoveryTests.Features.AnalyticsStore;

internal static class UserAnalyticsDerivedProcessTrial
{
    private const string Prefix = "keyload-user-analytics-derived-process-";
    private const string Dotnet = "dotnet";
    private const string GuidFormat = "N";
    private const string RetainedRoot = "KeyLoad.UserAnalyticsOriginalProcessRoot";
    private const int SuccessExit = 0;
    private static readonly string[] Modes = [P.Prepare, P.Recover];

    internal static async Task RunAsync(CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, deadline.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            foreach (var mode in Modes)
            {
                var killed = mode == P.Prepare;
                active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                    expectAcknowledgement: killed);
                active.Start(Start(root, mode));
                active.ThrowStartupFailure();
                await CompleteAsync(active, killed, linked.Token);
                await UserAnalyticsDerivedProcessFiles.RequireReleasedAsync(root, linked.Token);
                UserAnalyticsDerivedProcessFiles.RequireDerived(root, abandoned: killed);
                active.CloseNativeProcessAfterJoin();
                active = null;
            }
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure)) { primary = failure; }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure)) { primary = failure; }
        await CleanupAsync(root, active, primary);
    }

    private static async Task CompleteAsync(CommandIdempotencyProcessChild child, bool killed, CancellationToken token)
    {
        if (killed)
        {
            await child.WaitForAcknowledgementAsync(token);
            await child.StopAndJoinAsync(token);
            return;
        }
        await child.WaitAndJoinAsync(token);
        if (child.ExitCode != SuccessExit)
        {
            throw new InvalidOperationException(P.Invalid);
        }
    }

    private static ProcessStartInfo Start(string root, string mode)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, mode })
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static async Task CleanupAsync(string root, CommandIdempotencyProcessChild? active, Exception? primary)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        var failures = new List<Exception>();
        if (active is not null)
        {
            await CommandIdempotencyProcess.SettleActiveAsync(active, failures, deadline.Token);
        }

        if (active is { IsSettled: false })
        {
            failures.Add(new IOException(P.Invalid));
        }

        if (primary is null && failures.Count == SuccessExit)
        {
            await ServerFailureObserver.ObserveAsync(
                () => UserAnalyticsDerivedProcessFiles.RequireReleasedAsync(root, deadline.Token), failures);
            if (failures.Count == SuccessExit)
            {
                await ServerFailureObserver.ObserveAsync(
                    () => StoragePublicationRecoveryTests.DeleteTrialAsync(root, deadline.Token), failures);
            }
        }
        if (primary is not null)
        {
            primary.Data[RetainedRoot] = root;
        }

        foreach (var failure in failures)
        {
            failure.Data[RetainedRoot] = root;
        }

        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}
