using System.Diagnostics;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed class AggregateReplayProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public async Task AcEvent008SnapshotAndOutcomeRecoverAtomicallyWithStableRetry(CommitStage stage, int index)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-aggregate-replay-crash-" + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var process = StartCrashProcess(root, stage, index);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            await KillAtBoundaryAsync(process, root, timeout.Token);
            await AssertRecoveredSnapshotAndRetryAsync(root, stage, timeout.Token);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await StopAndDeleteAsync(process, root, cleanup.Token);
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
                     index.ToString(System.Globalization.CultureInfo.InvariantCulture), AggregateReplayCrashScenario.Mode })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start aggregate replay crash host.");
    }

    private static async Task KillAtBoundaryAsync(Process process, string root, CancellationToken cancellationToken)
    {
        await Assert.That(await process.StandardOutput.ReadLineAsync(cancellationToken)).IsEqualTo(CrashFixtureValues.CrashMarker);
        process.Kill();
        await process.WaitForExitAsync(cancellationToken);
        await KilledProcessFileReadiness.WaitAsync(root, cancellationToken);
    }

    private static async Task AssertRecoveredSnapshotAndRetryAsync(string root, CommitStage stage,
        CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, AggregateReplayCrashScenario.OperationFile), cancellationToken));
        var before = ReadReplay(database);
        var recoveredOutcome = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        var observedCommitted = before.Snapshot is not null;
        await Assert.That(recoveredOutcome is not null).IsEqualTo(observedCommitted);
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(observedCommitted).IsTrue();
        }
        if (before.Snapshot is not null)
        {
            await Assert.That(before.Snapshot!.SnapshotVersion).IsEqualTo(AggregateReplayCrashScenario.FirstSnapshotVersion);
        }
        await AssertSourceBytesUnchangedAsync(store, root);

        var first = database.Apply(operation).Get<CommitReceipt>();
        if (recoveredOutcome is not null)
        {
            await Assert.That(JsonDefaults.Serialize(recoveredOutcome).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
        }
        var replayed = ReadReplay(database);
        await Assert.That(replayed.Snapshot).IsNotNull();
        await Assert.That(replayed.Snapshot!.SnapshotVersion).IsEqualTo(AggregateReplayCrashScenario.FirstSnapshotVersion);
        await Assert.That(replayed.Snapshot.StateJson).IsEqualTo(AggregateReplayCrashScenario.StateJson);
        if (before.Snapshot is not null)
        {
            await Assert.That(replayed.Snapshot).IsEqualTo(before.Snapshot);
        }
        await Assert.That(replayed.Events).HasSingleItem();
        await Assert.That(replayed.Events[0].Data.EventId).IsEqualTo(AggregateReplayCrashScenario.EventId);
        await Assert.That(replayed.Events[0].Data.PayloadJson).IsEqualTo(AggregateReplayCrashScenario.EventPayloadJson);
        await Assert.That(replayed.Events[0].Revision).IsEqualTo(AggregateReplayCrashScenario.EventRevision);
        await AssertSourceBytesUnchangedAsync(store, root);

        var position = store.Position;
        var second = database.Apply(operation).Get<CommitReceipt>();
        var durable = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(second).AsSpan().SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
        await Assert.That(durable).IsNotNull();
        await Assert.That(JsonDefaults.Serialize(durable!).AsSpan().SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await AssertSourceBytesUnchangedAsync(store, root);
        await Assert.That(ReadReplay(database).Snapshot!.SnapshotVersion)
            .IsEqualTo(AggregateReplayCrashScenario.FirstSnapshotVersion);
    }

    private static AggregateReplayPage ReadReplay(DatabaseEngine database)
        => database.ReadAggregateReplay(CrashFixtureValues.Principal,
            new(new(AggregateReplayCrashScenario.Partition, AggregateReplayCrashScenario.StreamSet,
                AggregateReplayCrashScenario.StreamId, AggregateReplayCrashScenario.Generation),
                AggregateReplayCrashScenario.Reducer,
                StateSchemaVersion: AggregateReplayCrashScenario.StateSchemaVersion,
                MaximumEvents: AggregateReplayCrashScenario.ReplayTailLimit));

    private static async Task AssertSourceBytesUnchangedAsync(ZoneTreeStore store, string root)
    {
        var keys = new[]
        {
            KeySpace.Partition(AggregateReplayCrashScenario.StreamHeadKeySpace, AggregateReplayCrashScenario.Partition,
                AggregateReplayCrashScenario.StreamSet, AggregateReplayCrashScenario.StreamId),
            KeySpace.Partition(AggregateReplayCrashScenario.EventKeySpace, AggregateReplayCrashScenario.Partition,
                AggregateReplayCrashScenario.StreamSet, AggregateReplayCrashScenario.StreamId,
                AggregateReplayCrashScenario.Generation, AggregateReplayCrashScenario.EventRevision),
            KeySpace.Partition(AggregateReplayCrashScenario.EventIdentityKeySpace, AggregateReplayCrashScenario.Partition,
                AggregateReplayCrashScenario.StreamSet, AggregateReplayCrashScenario.StreamId,
                AggregateReplayCrashScenario.Generation,
                AggregateReplayCrashScenario.EventId)
        };
        var files = new[] { AggregateReplayCrashScenario.HeadFile, AggregateReplayCrashScenario.EventFile,
            AggregateReplayCrashScenario.IdentityFile };
        var current = store.Read(view => keys.Select(key => view.ReadOwnedValue(key)).ToArray());
        for (var index = 0; index < files.Length; index++)
        {
            var expected = await File.ReadAllBytesAsync(Path.Combine(root, files[index]));
            await Assert.That(current[index]!.AsSpan().SequenceEqual(expected)).IsTrue();
        }
    }

    private static async Task StopAndDeleteAsync(Process process, string root, CancellationToken cancellationToken)
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
