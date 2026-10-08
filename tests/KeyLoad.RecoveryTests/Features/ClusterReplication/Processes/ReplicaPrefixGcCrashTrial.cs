using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcCrashTrial
{
    private const string RootPrefix = "keyload-replica-prefix-gc-";
    private const string Dotnet = "dotnet";
    private const string IncarnationFormat = "D";
    private static readonly Guid Incarnation = new("4c00fd96-12a5-4d82-b295-85bdd3432d0e");
    private const string RetainedRootKey = "KeyLoad.ReplicaPrefixGc.RetainedRoot";
    private const string UnsettledChild = "The original replica-prefix GC child or readers did not settle.";

    internal static async Task RunAsync(CommitStage stage, int index, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
            CrashFixtureValues.CrashMarker);
        Exception? primary = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        try
        {
            child.Start(CreateStart(root, stage, index));
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(linked.Token);
            await child.StopAndJoinAsync(linked.Token);
            RecoveryFileInventory.AssertNativeHandlesReleased(root);
            child.CloseNativeProcessAfterJoin();
            await ReplicaPrefixGcRecoveryOracle.VerifyAsync(root, stage, linked.Token);
            RecoveryFileInventory.AssertNativeHandlesReleased(root);
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
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        var failures = new List<Exception>();
        await CommandIdempotencyProcess.SettleActiveAsync(child, failures, cleanup.Token);
        if (!child.IsSettled)
        { failures.Add(new IOException(UnsettledChild)); }
        if (primary is null && failures.Count == 0)
        { await ReplicaPrefixGcTrialCleanup.DeleteAsync(root, failures, cleanup.Token); }
        foreach (var failure in failures)
        { failure.Data[RetainedRootKey] = root; }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }

    private static ProcessStartInfo CreateStart(string root, CommitStage stage, int index)
    {
        var start = new ProcessStartInfo(Dotnet)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(),
            index.ToString(CultureInfo.InvariantCulture), ReplicaPrefixGcCrashContract.Mode, Incarnation.ToString(IncarnationFormat) })
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
