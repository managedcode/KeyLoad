using System.Diagnostics;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

public sealed partial class RecoveryTests
{
    [Theory]
    [InlineData(CommitStage.HeaderWritten, 0)]
    [InlineData(CommitStage.PayloadWritten, 0)]
    [InlineData(CommitStage.JournalFlushed, 0)]
    [InlineData(CommitStage.MutationApplied, 0)]
    [InlineData(CommitStage.MutationApplied, 3)]
    [InlineData(CommitStage.MutationApplied, 6)]
    [InlineData(CommitStage.ApplyCompleted, 0)]
    public async Task ProjectionCrashRecoversEffectsOutboxReceiptOutcomeAndCheckpointTogether(CommitStage stage, int index)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-projection-crash-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(), index.ToString(System.Globalization.CultureInfo.InvariantCulture), "projection-processing" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Equal("crash-point", await process.StandardOutput.ReadLineAsync(timeout.Token));
            process.Kill(); await process.WaitForExitAsync(timeout.Token); await WaitForKilledProcessFilesAsync(root, timeout.Token);
            using var store = new ZoneTreeStore(new(root)); var database = new DatabaseEngine(store, new AuthorizationPolicy());
            var status = database.GetOutboxStatus("root", ProjectionCrashScenario.Partition); var checkpoint = status.Consumers.Single().Checkpoint;
            var effect = database.GetDocument("root", new(ProjectionCrashScenario.Partition, "projection", "effect"));
            Assert.True(checkpoint is 0 or 1); Assert.Equal(checkpoint == 0, effect is null); Assert.Equal(checkpoint + 1, status.Head.Tail);
            Assert.Equal(checkpoint == 0, database.Outcome("root", ProjectionCrashScenario.CommandId) is null);
            if (stage >= CommitStage.JournalFlushed) Assert.Equal(1, checkpoint);
            var original = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(root, "processing-command.json"), timeout.Token));
            var recovered = database.Apply(original with { EvaluatedAt = DateTimeOffset.UtcNow }).Get<ProjectionBatchResult>();
            var request = JsonDefaults.Deserialize<CommitProjectionBatchRequest>(System.Text.Encoding.UTF8.GetBytes(original.PayloadJson));
            var retryId = Guid.NewGuid(); var retried = database.Apply(new(retryId, OperationKind.CommitProjectionBatch, "root", DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(request with { CommandId = retryId }, JsonDefaults.Options))).Get<ProjectionBatchResult>();
            Assert.True(retried.AlreadyProcessed); Assert.Equal(recovered.Receipt.Token, retried.Receipt.Token);
            Assert.Equal(1, database.GetOutboxStatus("root", ProjectionCrashScenario.Partition).Consumers.Single().Checkpoint);
            Assert.Equal(2, database.GetOutboxStatus("root", ProjectionCrashScenario.Partition).Head.Tail);
            Assert.Equal(1, database.GetDocument("root", new(ProjectionCrashScenario.Partition, "projection", "effect"))!.Revision);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
            if (Directory.Exists(root)) await DeleteTrialAsync(root, TestContext.Current.CancellationToken);
        }
    }
}
