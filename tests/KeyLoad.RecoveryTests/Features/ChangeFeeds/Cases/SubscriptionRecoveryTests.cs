using System.Diagnostics;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class SubscriptionProcessRecoveryTests
{
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
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var process = Process.Start(CreateCrashProcessStartInfo(root, stage, index))!;
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        try
        {
            await KillAtCrashPointAsync(process, root, timeout.Token);
            await AssertRecoveredProcessingAsync(root, stage, timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            { process.Kill(); await process.WaitForExitAsync(cancellationToken); }
            if (Directory.Exists(root))
            {
                await StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken);
            }
        }
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
                     index.ToString(System.Globalization.CultureInfo.InvariantCulture), "subscription-processing"
                 })
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static async Task KillAtCrashPointAsync(Process process, string root, CancellationToken cancellationToken)
    {
        await Assert.That(await process.StandardOutput.ReadLineAsync(cancellationToken)).IsEqualTo("crash-point");
        process.Kill();
        await process.WaitForExitAsync(cancellationToken);
        await StorageRecoveryProcessTests.WaitForKilledProcessFilesAsync(root, cancellationToken);
    }

    private static async Task AssertRecoveredProcessingAsync(string root, CommitStage stage,
        CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        var subscription = SubscriptionCrashScenario.Subscription;
        var checkpoint = database.GetSubscription("root", subscription).Checkpoint;
        var document = database.GetDocument("root", new(SubscriptionCrashScenario.Partition, "orders", "effect"), cancellationToken: cancellationToken);
        await Assert.That(checkpoint is 0 or 1).IsTrue();
        await Assert.That(document is null).IsEqualTo(checkpoint == 0);
        var outcome = database.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(SubscriptionCrashScenario.Partition,
            "root", SubscriptionCrashScenario.CommandId)));
        await Assert.That(outcome is null).IsEqualTo(checkpoint == 0);
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(checkpoint).IsEqualTo(1);
        }

        var original = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, "processing-command.json"), cancellationToken));
        var recovered = database.Apply(original with { EvaluatedAt = TimeProvider.System.GetUtcNow() })
            .Get<SubscriptionProcessingResult>();
        await Assert.That(database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
        await Assert.That(database.GetDocument("root", new(SubscriptionCrashScenario.Partition, "orders", "effect"), cancellationToken: cancellationToken)!
            .Revision).IsEqualTo(1);
        await AssertRetryReceiptAsync(database, subscription, original, recovered);
    }

    private static async Task AssertRetryReceiptAsync(DatabaseEngine database, SubscriptionRef subscription,
        ReplicatedOperation original, SubscriptionProcessingResult recovered)
    {
        var request = JsonDefaults.Deserialize<SubscriptionProcessingRequest>(System.Text.Encoding.UTF8.GetBytes(original.PayloadJson));
        var retryId = Guid.NewGuid();
        var retry = database.Apply(new(retryId, OperationKind.SubscriptionProcessing, "root", TimeProvider.System.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(request with { CommandId = retryId }, JsonDefaults.Options)))
            .Get<SubscriptionProcessingResult>();
        await Assert.That(retry.AlreadyProcessed).IsTrue();
        await Assert.That(retry.OriginalEffectsToken).IsEqualTo(recovered.OriginalEffectsToken);
        await Assert.That(database.GetDocument("root", new(SubscriptionCrashScenario.Partition, "orders", "effect"))!.Revision).IsEqualTo(1);
        await Assert.That(database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
    }
}
