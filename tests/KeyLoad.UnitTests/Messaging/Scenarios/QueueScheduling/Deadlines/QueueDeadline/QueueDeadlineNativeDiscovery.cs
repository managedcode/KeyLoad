using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeDiscovery
{
    internal static async Task RequireAsync(TestDatabase fixture, QueueLaneRef lane,
        AdvanceQueueDeadline expected, DateTimeOffset due, CancellationToken token)
    {
        var clock = new DueDiscoveryClock();
        var database = new DatabaseEngine(fixture.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.DueWork(new()
            {
                MaximumRecordsPerPage = QueueDeadlineNativeProtocol.MinimumPageRecords
            }),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(),
            UnavailablePartitionMovementCheckpointVerifier.Instance, timeProvider: clock);
        var original = QueueRetryColdAssertions.LaneBytes(fixture.Store, lane);
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        var position = fixture.Store.Position;
        QueueDeadlineCursor? cursor = null;
        QueueDeadlineHint? observed = null;
        for (var index = (int)QueueDeadlineNativeProtocol.Initial;
            index < QueueDeadlineNativeProtocol.MaximumDiscoveryPages && observed is null; index++)
        {
            if (cursor is { Next: QueueDeadlineCursor.MetadataIndex } && !clock.ExpiredAfterNativeProgress)
            {
                var baseline = fixture.Store.GetReadDiagnostics().RangeExaminedBytes;
                clock.Arm(() => fixture.Store.GetReadDiagnostics().RangeExaminedBytes > baseline,
                    database.DueDiscoveryDeadline);
                var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                    QueueDeadlineDiscovery.ReadPage(database, cursor, due, token));
                await Assert.That(clock.ExpiredAfterNativeProgress).IsTrue();
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.DeadlineExceeded);
                clock.Disarm();
                await Assert.That(fixture.Store.Position).IsEqualTo(position);
                await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity).SequenceEqual(identity)).IsTrue();
                await Assert.That(QueueRetryColdAssertions.LaneBytes(fixture.Store, lane))
                    .IsEquivalentTo(original, CollectionOrdering.Matching);
            }
            var page = QueueDeadlineDiscovery.ReadPage(database, cursor, due, token);
            cursor = page.Cursor;
            await Assert.That(page.ExaminedRecords).IsLessThanOrEqualTo(QueueDeadlineNativeProtocol.MinimumPageRecords);
            await Assert.That(page.ExaminedBytes).IsLessThanOrEqualTo(database.DueExecution.MaximumRangeBytes);
            await Assert.That(page.Rejected).IsEmpty();
            if (!page.Jobs.IsEmpty)
            {
                observed = page.Jobs.Single();
            }
        }
        await Assert.That(clock.ExpiredAfterNativeProgress).IsTrue();
        await Assert.That(observed).IsEqualTo(new QueueDeadlineHint(lane, expected));
        await Assert.That(QueueRetryColdAssertions.LaneBytes(fixture.Store, lane))
            .IsEquivalentTo(original, CollectionOrdering.Matching);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.FromResult(
            QueueDeadlineDiscovery.ReadPage(database, cursor, due, cancelled.Token)));
        await Assert.That(QueueRetryColdAssertions.LaneBytes(fixture.Store, lane))
            .IsEquivalentTo(original, CollectionOrdering.Matching);
    }
}
