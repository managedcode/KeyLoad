using System.Diagnostics;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
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
    public async Task SubscriptionProcessingCrashRecoversEffectsInboxOutcomeAndCheckpointTogether(CommitStage stage, int index)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-subscription-crash-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(), index.ToString(System.Globalization.CultureInfo.InvariantCulture), "subscription-processing" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Equal("crash-point", await process.StandardOutput.ReadLineAsync(timeout.Token));
            process.Kill(); await process.WaitForExitAsync(timeout.Token); await WaitForKilledProcessFilesAsync(root, timeout.Token);
            using var store = new ZoneTreeStore(new(root)); var database = new DatabaseEngine(store, new AuthorizationPolicy());
            var subscription = SubscriptionCrashScenario.Subscription;
            var checkpoint = database.GetSubscription("root", subscription).Checkpoint;
            var document = database.GetDocument("root", new(SubscriptionCrashScenario.Partition, "orders", "effect"));
            Assert.True(checkpoint is 0 or 1); Assert.Equal(checkpoint == 0, document is null);
            var outcome = database.Outcome("root", SubscriptionCrashScenario.CommandId);
            Assert.Equal(checkpoint == 0, outcome is null);
            if (stage >= CommitStage.JournalFlushed) Assert.Equal(1, checkpoint);
            var original = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(root, "processing-command.json"), timeout.Token));
            var recovered = database.Apply(original with { EvaluatedAt = DateTimeOffset.UtcNow }).Get<SubscriptionProcessingResult>();
            Assert.Equal(1, database.GetSubscription("root", subscription).Checkpoint);
            Assert.Equal(1, database.GetDocument("root", new(SubscriptionCrashScenario.Partition, "orders", "effect"))!.Revision);
            var request = JsonDefaults.Deserialize<SubscriptionProcessingRequest>(System.Text.Encoding.UTF8.GetBytes(original.PayloadJson));
            var retryId = Guid.NewGuid(); var retry = database.Apply(new(retryId, OperationKind.SubscriptionProcessing, "root", DateTimeOffset.UtcNow,
                System.Text.Json.JsonSerializer.Serialize(request with { CommandId = retryId }, JsonDefaults.Options))).Get<SubscriptionProcessingResult>();
            Assert.True(retry.AlreadyProcessed); Assert.Equal(recovered.OriginalEffectsToken, retry.OriginalEffectsToken);
            Assert.Equal(1, database.GetDocument("root", new(SubscriptionCrashScenario.Partition, "orders", "effect"))!.Revision);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
            if (Directory.Exists(root)) await DeleteTrialAsync(root, TestContext.Current.CancellationToken);
        }
    }
}
