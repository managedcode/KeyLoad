using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleRetentionRecoveryIdentityAssertions
{
    private const string Principal = CrashFixtureValues.Principal;

    internal static async Task VerifyAsync(DatabaseEngine database, ZoneTreeStore store)
    {
        await VerifyExpiredIdentityAndSequenceAsync(database, store);
        await VerifyFreshIdentityAsync(database);
    }

    private static async Task VerifyExpiredIdentityAndSequenceAsync(DatabaseEngine database, ZoneTreeStore store)
    {
        var duplicate = Append(database, SampleRetentionCrashScenario.EventId(0),
            SampleRetentionCrashScenario.Timestamp(0), SampleRetentionCrashScenario.Value(0));
        _ = database.Apply(duplicate).Get<CommitReceipt>();
        var afterDuplicate = store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(SampleRetentionCrashScenario.SequenceKey())!));
        await Assert.That(afterDuplicate).IsEqualTo(SampleRetentionCrashScenario.SampleCount);

        var expired = Append(database, "new-expired-id", SampleRetentionCrashScenario.Start, 9);
        var expiredFailure = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(expired).Get<CommitReceipt>());
        await Assert.That(expiredFailure.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        var changed = Append(database, SampleRetentionCrashScenario.EventId(0),
            SampleRetentionCrashScenario.Timestamp(0), 9);
        var changedFailure = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(changed).Get<CommitReceipt>());
        await Assert.That(changedFailure.Code).IsEqualTo(ErrorCode.Conflict);
    }

    private static async Task VerifyFreshIdentityAsync(DatabaseEngine database)
    {
        var fresh = Append(database, "new-live-id", SampleRetentionCrashScenario.Cutoff.AddTicks(1), 16);
        _ = database.Apply(fresh).Get<CommitReceipt>();
        var point = database.ReadSamples(Principal, SampleRetentionCrashScenario.Partition,
            SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId,
            SampleRetentionCrashScenario.Cutoff, SampleRetentionCrashScenario.Cutoff.AddTicks(1), 10);
        await Assert.That(point.Select(sample => sample.Sequence)).IsEquivalentTo(new long[] { 4, 5 }, CollectionOrdering.Matching);
        await Assert.That(point.Select(sample => sample.Sample.Value)).IsEquivalentTo(new[] { 8d, 16d }, CollectionOrdering.Matching);
    }

    private static Command<DatabaseCommand> Append(DatabaseEngine database, string eventId, DateTimeOffset timestamp, double value)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, SampleRetentionCrashScenario.Partition,
            [new AppendSamples(SampleRetentionCrashScenario.SeriesSet, SampleRetentionCrashScenario.SeriesId,
                [new(eventId, timestamp, value)], SampleRetentionCrashScenario.Tags)]);
        return CrashDatabase.Operation(database, OperationKind.Batch, request, id);
    }
}
