using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core;
using KeyLoad.Security;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeDiscovery
{
    internal static async Task RequireAsync(TestDatabase fixture, QueueLaneRef lane,
        AdvanceQueueDeadline expected, DateTimeOffset due, CancellationToken token)
    {
        var database = new DatabaseEngine(fixture.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.DueWork(new() { MaximumRecordsPerPage = QueueDeadlineNativeProtocol.MinimumPageRecords }),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(),
            UnavailablePartitionMovementCheckpointVerifier.Instance);
        var original = QueueRetryColdAssertions.LaneBytes(fixture.Store, lane);
        QueueDeadlineCursor? cursor = null;
        QueueDeadlineHint? observed = null;
        for (var index = (int)QueueDeadlineNativeProtocol.Initial;
            index < QueueDeadlineNativeProtocol.MaximumDiscoveryPages && observed is null; index++)
        {
            var page = QueueDeadlineDiscovery.ReadPage(database, cursor, due, token);
            cursor = page.Cursor;
            await Assert.That(page.ExaminedRecords).IsLessThanOrEqualTo(QueueDeadlineNativeProtocol.MinimumPageRecords);
            await Assert.That(page.ExaminedBytes).IsLessThanOrEqualTo(database.DueExecution.MaximumRangeBytes);
            await Assert.That(page.Rejected).IsEmpty();
            if (!page.Jobs.IsEmpty)
            { observed = page.Jobs.Single(); }
        }
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
