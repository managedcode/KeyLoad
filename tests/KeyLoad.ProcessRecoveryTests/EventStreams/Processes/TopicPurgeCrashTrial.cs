using System.Diagnostics;
using System.Globalization;
using KeyLoad.ProcessRecoveryTests.DocumentStorage;
using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class TopicPurgeCrashTrial
{
    private const string RootPrefix = "keyload-topic-purge-crash-";
    private const string Dotnet = "dotnet";
    private const string RetainedRootKey = "KeyLoad.TopicPurgeCrash.RetainedRoot";
    private const string UnsettledChild = "The original topic purge child or readers did not settle.";

    internal static async Task RunAsync(CommitStage stage, int index, bool pinned, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString("N"));
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
            CrashFixtureValues.CrashMarker);
        Exception? primary = null;
        using var timeout = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CommandTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        try
        {
            child.Start(CreateStart(root, stage, index, pinned));
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(linked.Token);
            await child.StopAndJoinAsync(linked.Token);
            RecoveryFileInventory.AssertHandlesReleased(root);
            child.CloseProcessAfterJoin();
            await TopicPurgeRecoveryOracle.VerifyAsync(root, stage, linked.Token);
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
        using var cleanup = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CleanupTimeout, TimeProvider.System);
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
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }

    private static ProcessStartInfo CreateStart(string root, CommitStage stage, int index, bool pinned)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode, root, stage.ToString(),
            index.ToString(CultureInfo.InvariantCulture), pinned ? TopicPurgeCrashContract.Mode + "-pinned" : TopicPurgeCrashContract.Mode })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
