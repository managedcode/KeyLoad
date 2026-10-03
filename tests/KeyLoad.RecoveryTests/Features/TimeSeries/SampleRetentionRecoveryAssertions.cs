using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleRetentionRecoveryAssertions
{
    private const string Principal = CrashFixtureValues.Principal;

    internal static async Task VerifyRecoveredPageAsync(string root, CommitStage stage,
        CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root));
        var database = new DatabaseEngine(store, new AuthorizationPolicy());
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, SampleRetentionCrashScenario.OperationFile), cancellationToken));
        var recoveredStatus = database.ReadSampleRetention(Principal, RetentionRequest());
        var committed = recoveredStatus.Before == SampleRetentionCrashScenario.Cutoff;
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
        var oldOutcome = database.Outcome(Principal, operation.Id);
        await Assert.That(oldOutcome is not null).IsEqualTo(committed);
        await VerifyPersistedBytesAsync(store, root, committed, cancellationToken);
        await VerifyStatusAndReadsAsync(database, committed, committed ?
            SampleRetentionCrashScenario.FirstPageDeletes : 0, committed);

        await ReplayFirstPageAsync(database, store, root, operation, oldOutcome, cancellationToken);
        await CompleteRetentionAsync(database, store, root, operation, cancellationToken);
        await SampleRetentionRecoveryIdentityAssertions.VerifyAsync(database, store);
    }

    private static async Task ReplayFirstPageAsync(DatabaseEngine database, ZoneTreeStore store, string root,
        ReplicatedOperation operation, OperationOutcome? oldOutcome, CancellationToken cancellationToken)
    {
        var first = database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(first.Mutations).HasSingleItem();
        if (oldOutcome is not null)
        {
            var recovered = oldOutcome.Get<CommitReceipt>();
            await Assert.That(recovered.Token).IsEqualTo(first.Token);
            await Assert.That(JsonDefaults.Serialize(recovered).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
        }
        var afterFirstReplay = store.Position;
        var firstStatus = database.ReadSampleRetention(Principal, RetentionRequest());
        await Assert.That(firstStatus).IsEqualTo(new(SampleRetentionCrashScenario.Cutoff,
            SampleRetentionCrashScenario.FirstPageDeletes, true));
        var firstPageAgain = database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(firstPageAgain.Token).IsEqualTo(first.Token);
        await Assert.That(store.Position).IsEqualTo(afterFirstReplay);
        await VerifyPersistedBytesAsync(store, root, committed: true, cancellationToken: cancellationToken);
        await VerifyStatusAndReadsAsync(database, committed: true,
            SampleRetentionCrashScenario.FirstPageDeletes, hasMore: true);
    }

    private static async Task CompleteRetentionAsync(DatabaseEngine database, ZoneTreeStore store, string root,
        ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        var nextId = Guid.Parse("76d20dd7-5b66-4de4-9e63-ed09f0d19930");
        var next = new ReplicatedOperation(nextId, OperationKind.Batch, Principal,
            operation.EvaluatedAt, JsonDefaults.Serialize(new CommandRequest(nextId,
                SampleRetentionCrashScenario.Partition,
                [new ExpireSamples(SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId,
                    SampleRetentionCrashScenario.Cutoff, SampleRetentionCrashScenario.RepeatPageDeletes)])));
        _ = database.Apply(next).Get<CommitReceipt>();
        var complete = database.ReadSampleRetention(Principal, RetentionRequest());
        await Assert.That(complete).IsEqualTo(new(SampleRetentionCrashScenario.Cutoff,
            SampleRetentionCrashScenario.SampleCount - 1, false));
        await VerifyPersistedBytesAsync(store, root, committed: true, cancellationToken: cancellationToken,
            completedPages: true);
        await VerifyStatusAndReadsAsync(database, committed: true,
            SampleRetentionCrashScenario.SampleCount - 1, hasMore: false);
    }

    private static async Task VerifyPersistedBytesAsync(ZoneTreeStore store, string root, bool committed,
        CancellationToken cancellationToken, bool completedPages = false)
    {
        var removedCount = committed
            ? completedPages ? SampleRetentionCrashScenario.SampleCount - 1 : SampleRetentionCrashScenario.FirstPageDeletes
            : 0;
        for (var index = 0; index < SampleRetentionCrashScenario.SampleCount; index++)
        {
            var expectedPoint = await File.ReadAllBytesAsync(Path.Combine(root,
                SampleRetentionCrashScenario.SampleFile(index)), cancellationToken);
            var actualPoint = store.Read(view => view.ReadOwnedValue(SampleRetentionCrashScenario.SampleKey(index)));
            await Assert.That(actualPoint is null).IsEqualTo(index < removedCount);
            if (actualPoint is not null)
            {
                await Assert.That(actualPoint.AsSpan().SequenceEqual(expectedPoint)).IsTrue();
            }

            var expectedId = await File.ReadAllBytesAsync(Path.Combine(root,
                SampleRetentionCrashScenario.IdentityFile(index)), cancellationToken);
            var actualId = store.Read(view => view.ReadOwnedValue(SampleRetentionCrashScenario.IdentityKey(index)));
            await Assert.That(actualId is not null).IsTrue();
            await Assert.That(actualId!.AsSpan().SequenceEqual(expectedId)).IsTrue();
        }

        var expectedSequence = await File.ReadAllBytesAsync(Path.Combine(root,
            SampleRetentionCrashScenario.SequenceFile), cancellationToken);
        var actualSequence = store.Read(view => view.ReadOwnedValue(SampleRetentionCrashScenario.SequenceKey()));
        await Assert.That(actualSequence).IsNotNull();
        await Assert.That(actualSequence!.AsSpan().SequenceEqual(expectedSequence)).IsTrue();
    }

    private static async Task VerifyStatusAndReadsAsync(DatabaseEngine database, bool committed,
        long purgedCount, bool hasMore)
    {
        var status = database.ReadSampleRetention(Principal, RetentionRequest());
        await Assert.That(status.Before).IsEqualTo(committed ? SampleRetentionCrashScenario.Cutoff : null);
        await Assert.That(status.PurgedCount).IsEqualTo(purgedCount);
        await Assert.That(status.HasMore).IsEqualTo(hasMore);

        await VerifyRangeAndLatestAsync(database, committed);
        await VerifyAggregatesAsync(database, committed);
    }

    private static async Task VerifyRangeAndLatestAsync(DatabaseEngine database, bool committed)
    {
        var all = Enumerable.Range(0, SampleRetentionCrashScenario.SampleCount)
            .Where(index => !committed || index == SampleRetentionCrashScenario.SampleCount - 1)
            .ToArray();
        var range = database.ReadSamples(Principal, SampleRetentionCrashScenario.Partition,
            SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId,
            SampleRetentionCrashScenario.Start, SampleRetentionCrashScenario.Start.AddMinutes(4), 10);
        await Assert.That(range.Select(sample => sample.Sample.EventId)).IsEquivalentTo(
            all.Select(SampleRetentionCrashScenario.EventId), CollectionOrdering.Matching);
        await Assert.That(range.Select(sample => sample.Sequence)).IsEquivalentTo(
            all.Select(index => (long)index + 1), CollectionOrdering.Matching);

        var latest = database.ReadLatestSample(Principal, new(SampleRetentionCrashScenario.Partition,
            SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId));
        await Assert.That(latest.Sample?.Sample.EventId)
            .IsEqualTo(SampleRetentionCrashScenario.EventId(SampleRetentionCrashScenario.SampleCount - 1));
    }

    private static async Task VerifyAggregatesAsync(DatabaseEngine database, bool committed)
    {
        var all = Enumerable.Range(0, SampleRetentionCrashScenario.SampleCount)
            .Where(index => !committed || index == SampleRetentionCrashScenario.SampleCount - 1)
            .ToArray();
        var aggregate = database.AggregateSamples(Principal, new(SampleRetentionCrashScenario.Partition,
            SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId,
            SampleRetentionCrashScenario.Start, SampleRetentionCrashScenario.Start.AddMinutes(4)));
        var expectedValues = all.Select(SampleRetentionCrashScenario.Value).ToArray();
        await Assert.That(aggregate).IsEqualTo(Oracle(expectedValues));
        var windows = database.AggregateSampleWindows(Principal, new(SampleRetentionCrashScenario.Partition,
            SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId,
            SampleRetentionCrashScenario.Start, SampleRetentionCrashScenario.Start.AddMinutes(4), TimeSpan.FromMinutes(1)));
        var expectedWindowCounts = Enumerable.Range(0, SampleRetentionCrashScenario.SampleCount)
            .Select(index => (long)(all.Contains(index) ? 1 : 0)).ToArray();
        for (var index = 0; index < SampleRetentionCrashScenario.SampleCount; index++)
        {
            var aggregateExpected = all.Contains(index)
                ? Oracle([SampleRetentionCrashScenario.Value(index)]) : new SampleAggregate(0, 0, null, null, null);
            await Assert.That(windows.Windows[index].From).IsEqualTo(SampleRetentionCrashScenario.Timestamp(index));
            await Assert.That(windows.Windows[index].UntilExclusive)
                .IsEqualTo(SampleRetentionCrashScenario.Timestamp(index + 1));
            await Assert.That(windows.Windows[index].Aggregate).IsEqualTo(aggregateExpected);
            await Assert.That(windows.Windows[index].Aggregate.Count).IsEqualTo(expectedWindowCounts[index]);
        }
    }

    private static SampleAggregate Oracle(double[] values)
    {
        if (values.Length == 0)
        {
            return new(0, 0, null, null, null);
        }
        var sum = values.Aggregate(0d, static (current, value) => current + value);
        return new(values.LongLength, sum, values.Min(), values.Max(), sum / values.LongLength);
    }

    private static ReadSampleRetentionRequest RetentionRequest()
        => new(SampleRetentionCrashScenario.Partition, SampleRetentionCrashScenario.SeriesSet,
            SampleRetentionCrashScenario.SeriesId);
}
