using System.Diagnostics;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ProjectionProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 3)]
    [Arguments(CommitStage.MutationApplied, 6)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public async Task ProjectionCrashRecoversEffectsOutboxReceiptOutcomeAndCheckpointTogether(CommitStage stage, int index)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-projection-crash-" + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var process = StartCrashProcess(root, stage, index);
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        try
        {
            await Assert.That(await process.StandardOutput.ReadLineAsync(timeout.Token)).IsEqualTo("crash-point");
            process.Kill();
            await process.WaitForExitAsync(timeout.Token);
            await StorageRecoveryProcessTests.WaitForKilledProcessFilesAsync(root, timeout.Token);
            using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(new() { MaxOutboxRecords = 1 }), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
            await AssertRecoveredProcessCutAsync(database, stage);
            await RetryRecoveredCommandAsync(root, database, timeout.Token);
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

    private static Process StartCrashProcess(string root, CommitStage stage, int index)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(),
                     index.ToString(System.Globalization.CultureInfo.InvariantCulture), "projection-processing" })
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the projection crash host.");
    }

    private static async Task AssertRecoveredProcessCutAsync(DatabaseEngine database, CommitStage stage)
    {
        var status = database.GetOutboxStatus("root", ProjectionCrashScenario.Partition);
        var checkpoint = status.Consumers.Single().Checkpoint;
        var effect = database.GetDocument("root", new(ProjectionCrashScenario.Partition, "projection", "effect"));
        await Assert.That(checkpoint is 0 or 1).IsTrue();
        await Assert.That(effect is null).IsEqualTo(checkpoint == 0);
        await Assert.That(status.Head.Tail).IsEqualTo(checkpoint + 1);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(ProjectionCrashScenario.Partition,
            "root", ProjectionCrashScenario.CommandId))) is null).IsEqualTo(checkpoint == 0);
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(checkpoint == 0 ? -1 : 1);
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(checkpoint).IsEqualTo(1);
        }
    }

    private static async Task RetryRecoveredCommandAsync(string root, DatabaseEngine database,
        CancellationToken cancellationToken)
    {
        var commandPath = Path.Combine(root, "processing-command.json");
        var original = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(commandPath, cancellationToken));
        var recovered = database.Apply(original with { EvaluatedAt = TimeProvider.System.GetUtcNow() }).Get<ProjectionBatchResult>();
        await AssertRetryReceiptAsync(database, original, recovered);
    }

    private static async Task AssertRetryReceiptAsync(DatabaseEngine database, ReplicatedOperation original,
        ProjectionBatchResult recovered)
    {
        var request = JsonDefaults.Deserialize<CommitProjectionBatchRequest>(System.Text.Encoding.UTF8.GetBytes(original.PayloadJson));
        var retryId = Guid.NewGuid();
        var retried = database.Apply(new(retryId, OperationKind.CommitProjectionBatch, "root", TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(request with { CommandId = retryId }, JsonDefaults.Options))).Get<ProjectionBatchResult>();
        var status = database.GetOutboxStatus("root", ProjectionCrashScenario.Partition);
        await Assert.That(retried.AlreadyProcessed).IsTrue();
        await Assert.That(retried.Receipt.Token).IsEqualTo(recovered.Receipt.Token);
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(1);
        await Assert.That(status.Consumers.Single().LastProgressReservationCut).IsEqualTo(1);
        await Assert.That(status.Head.Tail).IsEqualTo(2);
        await Assert.That(database.GetDocument("root", new(ProjectionCrashScenario.Partition, "projection", "effect"))!.Revision).IsEqualTo(1);
    }
}
