using System.Diagnostics;
using System.Globalization;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionProcessRecoveryTests
{
    private const string OutboxSpace = "outbox";
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 3)]
    [Arguments(CommitStage.MutationApplied, 6)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public async Task AcComp004006008ProcessKillRecoversOneCompositionCutAndStableRetry(CommitStage stage, int index)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-composition-crash-" + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var process = StartCrashProcess(root, stage, index);
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        try
        {
            await Assert.That(await process.StandardOutput.ReadLineAsync(timeout.Token)).IsEqualTo("crash-point");
            process.Kill();
            await process.WaitForExitAsync(timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            await AssertRecoveredAndRetriedAsync(root, stage, timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(cancellationToken);
            }
            if (Directory.Exists(root))
            {
                await StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken);
            }
        }
    }

    private static Process StartCrashProcess(string root, CommitStage stage, int index)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(),
                     index.ToString(CultureInfo.InvariantCulture), DatabaseCompositionCrashScenario.Mode })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the composition crash host.");
    }

    private static async Task AssertRecoveredAndRetriedAsync(string root, CommitStage stage, CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
        var tail = long.Parse(await File.ReadAllTextAsync(Path.Combine(root,
            DatabaseCompositionCrashScenario.SeedOutboxTailFile), cancellationToken), CultureInfo.InvariantCulture);
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, DatabaseCompositionCrashScenario.CommandFile), cancellationToken));
        var committed = await AssertAtomicRecoveredCutAsync(store, database, tail, operation, stage);
        var recoveredReceipt = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        var first = database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(first.Mutations.Length).IsEqualTo(2);
        if (committed)
        {
            await Assert.That(first.Token).IsEqualTo(recoveredReceipt!.Token);
        }
        await DatabaseCompositionRecoveryAssertions.AssertCommittedEffectsAsync(database, tail, first);
        await DatabaseCompositionRecoveryAssertions.AssertStableReplayAsync(database, operation, first);
    }

    private static async Task<bool> AssertAtomicRecoveredCutAsync(ZoneTreeStore store, DatabaseEngine database, long tail,
        ReplicatedOperation operation, CommitStage stage)
    {
        var partition = DatabaseCompositionCrashScenario.Partition;
        var from = new EntityRef(partition, DatabaseCompositionCrashScenario.Collection, DatabaseCompositionCrashScenario.First);
        var graph = database.Traverse(DatabaseCompositionCrashScenario.Principal, partition,
            DatabaseCompositionCrashScenario.Graph, from);
        var message = database.InspectMessage(DatabaseCompositionCrashScenario.Principal,
            new(partition, DatabaseCompositionCrashScenario.TargetQueue),
            DatabaseCompositionCrashScenario.MessagePrefix + DatabaseCompositionCrashScenario.EdgePrefix
            + DatabaseCompositionCrashScenario.SourceMessage);
        var outcome = OutcomeStoreOracle.Read(database.Store, operation);
        var observedTail = database.GetOutboxStatus(DatabaseCompositionCrashScenario.Principal, partition).Head.Tail;
        var outbox = store.Read(view => new[]
        {
            view.GetRecord<OutboxEntry>(KeySpace.Partition(OutboxSpace, partition, tail + 1)),
            view.GetRecord<OutboxEntry>(KeySpace.Partition(OutboxSpace, partition, tail + 2))
        });
        var committed = graph.Edges.Length == 1;
        await DatabaseCompositionRecoveryAssertions.AssertSeedUnchangedAsync(database);
        await Assert.That(graph.Edges.Length).IsEqualTo(committed ? 1 : 0);
        await Assert.That(message is not null).IsEqualTo(committed);
        await Assert.That(outcome is not null).IsEqualTo(committed);
        await Assert.That(outbox.All(entry => (entry is not null) == committed)).IsTrue();
        await Assert.That(observedTail).IsEqualTo(tail + (committed ? 2 : 0));
        if (committed)
        {
            await Assert.That(outbox[0]!.Mutation).IsTypeOf<UpsertEdge>();
            await Assert.That(outbox[1]!.Mutation).IsTypeOf<EnqueueMessage>();
            await Assert.That(outbox[0]!.Commit).IsEqualTo(outbox[1]!.Commit);
        }
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
        return committed;
    }
}
