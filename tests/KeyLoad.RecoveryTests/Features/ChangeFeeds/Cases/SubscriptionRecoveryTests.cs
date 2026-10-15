using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.Messaging;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class SubscriptionProcessRecoveryTests
{
    private const int OriginalProcessSeconds = 20;
    private const string RetainedRootKey = "KeyLoad.SubscriptionProcessing.RetainedRoot";

    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 3)]
    [Arguments(CommitStage.MutationApplied, 6)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public async Task SubscriptionProcessingCrashRecoversEffectsInboxOutcomeAndCheckpointTogether(CommitStage stage, int index)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-subscription-crash-" + Guid.NewGuid().ToString("N"));
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
            CrashFixtureValues.CrashMarker);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(OriginalProcessSeconds), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        Exception? primary = null;
        try
        {
            child.Start(CreateCrashProcessStartInfo(root, stage, index));
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(lifetime.Token);
            await child.StopAndJoinAsync(lifetime.Token);
            RecoveryFileInventory.AssertNativeHandlesReleased(root);
            child.CloseNativeProcessAfterJoin();
            await SubscriptionProcessingRecoveryAssertions.VerifyAsync(root, stage, lifetime.Token);
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
        {
            failures.Add(new IOException("The original subscription processing child or readers did not settle."));
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

    private static ProcessStartInfo CreateCrashProcessStartInfo(string root, CommitStage stage, int index)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(),
            index.ToString(System.Globalization.CultureInfo.InvariantCulture), CrashFixtureValues.SubscriptionMode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
