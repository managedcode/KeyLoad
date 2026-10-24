using System.Diagnostics;
using System.Globalization;
using KeyLoad.RecoveryTests.DocumentStorage;
using KeyLoad.RecoveryTests.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.EventStreams;

internal static class SharedDomainProcessTrial
{
    private const string RootPrefix = "keyload-shared-domain-crash-";
    private const string Dotnet = "dotnet";
    private const string RetainedRootKey = "KeyLoad.SharedDomainCrash.RetainedRoot";
    private const int FirstFailureIndex = 0;
    private const string UnsettledChild = "The original shared-domain child or readers did not settle.";

    internal static async Task RunAsync(CommitStage stage, int index, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString("N"));
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
            CrashFixtureValues.CrashMarker);
        Exception? primary = null;
        var policy = SharedDomainProcessConfiguration.Create().Value;
        using var timeout = new CancellationTokenSource(policy.ExecutionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        try
        {
            child.Start(CreateStart(root, stage, index));
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(linked.Token);
            await child.StopAndJoinAsync(linked.Token);
            RecoveryFileInventory.AssertHandlesReleased(root);
            child.CloseProcessAfterJoin();
            await SharedDomainRecoveryOracle.VerifyAsync(root, stage, linked.Token);
            RecoveryFileInventory.AssertHandlesReleased(root);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            primary = failure;
            primary.Data[RetainedRootKey] = root;
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            primary = failure;
            primary.Data[RetainedRootKey] = root;
        }
        using var cleanup = new CancellationTokenSource(policy.CleanupTimeout, TimeProvider.System);
        var failures = new List<Exception>();
        await CommandIdempotencyProcess.SettleActiveAsync(child, failures, cleanup.Token);
        if (!child.IsSettled)
        {
            failures.Add(new IOException(UnsettledChild));
        }
        if (primary is null && failures.Count == 0)
        {
            await CommandIdempotencyProcess.CleanTrialAsync(root, failures, cleanup.Token);
        }
        foreach (var failure in failures)
        {
            failure.Data[RetainedRootKey] = root;
        }
        if (child.IsSettled && child.IsDisposed && (primary is not null || failures.Count > FirstFailureIndex))
        {
            var original = primary ?? failures[FirstFailureIndex];
            await ServerFailureObserver.ObserveAsync(() => SharedDomainChildFailureObservation.ReportAsync(
                root, stage, index, original, cleanup.Token), failures).ConfigureAwait(false);
        }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }

    private static ProcessStartInfo CreateStart(string root, CommitStage stage, int index)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode, root, stage.ToString(),
            index.ToString(CultureInfo.InvariantCulture), SharedDomainCrashProtocol.Mode })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
