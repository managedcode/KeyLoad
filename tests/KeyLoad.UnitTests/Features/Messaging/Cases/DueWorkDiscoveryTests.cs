using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueWorkDiscoveryTests
{
    [Test]
    public async Task AlternatesPrefixesAndBoundsRealZoneTreePages()
    {
        using var fixture = new RecurringSagaDatabase();
        AddDueWork(fixture, DueWorkTestData.PageCount);
        var pages = DueWorkTestData.ReadPages(fixture, DueWorkTestData.PageCount * 2, out var hints);
        await Assert.That(hints.Length).IsEqualTo(DueWorkTestData.PageCount * 2);
        await Assert.That(hints.Select(hint => hint.Id).Distinct().Count()).IsEqualTo(hints.Length);
        await Assert.That(pages.Length >= 4).IsTrue();
        await Assert.That(pages[0].ScannedPrefix).IsEqualTo(DueWorkKind.Schedule);
        await Assert.That(pages[1].ScannedPrefix).IsEqualTo(DueWorkKind.Saga);
        await Assert.That(pages[2].ScannedPrefix).IsEqualTo(DueWorkKind.Schedule);
        await Assert.That(pages[3].ScannedPrefix).IsEqualTo(DueWorkKind.Saga);
        foreach (var page in pages)
        {
            await Assert.That(page.ExaminedRecords <= fixture.Database.DueExecution.MaximumRecordsPerPage).IsTrue();
            await Assert.That(page.AdmittedValueBytes <= fixture.Database.Limits.MaxBatchBytes).IsTrue();
            await Assert.That(page.ExaminedBytes >= page.AdmittedValueBytes).IsTrue();
            await Assert.That(page.ExaminedBytes <= fixture.Database.DueExecution.MaximumRangeBytes).IsTrue();
            await Assert.That(page.Rejected.IsEmpty).IsTrue();
        }
        await Assert.That(pages.Any(page => page.ExaminedBytes > page.AdmittedValueBytes)).IsTrue();
    }

    [Test]
    public async Task FiltersInactiveRowsAndReportsCorruptRows()
    {
        using var fixture = new RecurringSagaDatabase();
        var cancelledId = Guid.NewGuid();
        DueWorkTestData.AddDueSchedule(fixture, cancelledId);
        _ = fixture.Commit(fixture.Partition, new CancelRecurringSchedule(fixture.Queue, cancelledId, 1));
        var dueId = Guid.NewGuid();
        DueWorkTestData.AddDueSchedule(fixture, dueId);
        var futureId = Guid.NewGuid();
        var future = DueWorkTestData.Definition(fixture.Queue, futureId, DueWorkTestData.WakeAt.AddDays(1));
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(future, 0));
        var terminalId = Guid.NewGuid();
        _ = fixture.Commit(fixture.Partition, new CompareExchangeSaga(fixture.Queue, terminalId, 0,
            SagaPhase.Waiting, DueWorkTestData.EmptyJson));
        _ = fixture.Commit(fixture.Partition, new CompareExchangeSaga(fixture.Queue, terminalId, 1,
            SagaPhase.Completed, DueWorkTestData.EmptyJson));
        var sagaId = Guid.NewGuid();
        DueWorkTestData.AddWaitingSaga(fixture, sagaId);
        var badKeyId = Guid.NewGuid();
        DueWorkTestData.PutSchedule(fixture, badKeyId, Guid.NewGuid(), DueWorkTestData.EmptyJson);
        var malformedId = Guid.NewGuid();
        var malformedKey = KeySpace.Partition(DueWorkProtocol.ScheduleSpace, fixture.Partition,
            fixture.Queue.Queue, malformedId.ToString(DueWorkFields.GuidFormat));
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(malformedKey, [0xFF]);
            return true;
        });

        var pages = DueWorkTestData.ReadPages(fixture, 2, out var hints);
        await Assert.That(hints.Select(hint => hint.Id).Order().ToArray())
            .IsEquivalentTo(new[] { dueId, sagaId }.Order().ToArray());
        await Assert.That(hints.Any(hint => hint.Id == cancelledId || hint.Id == futureId || hint.Id == terminalId)).IsFalse();
        var rejected = pages.SelectMany(page => page.Rejected).ToArray();
        await Assert.That(rejected.Length).IsEqualTo(2);
        await Assert.That(rejected.All(record => record.Code == ErrorCode.Corruption)).IsTrue();
    }

    [Test]
    public async Task FixedTailDefersHigherKeysUntilTheFollowingSweep()
    {
        using var fixture = new RecurringSagaDatabase();
        var originalIds = Enumerable.Range(1, DueWorkTestData.PageCount)
            .Select(DueWorkTestData.OrderedId).ToArray();
        foreach (var id in originalIds)
        {
            DueWorkTestData.AddDueSchedule(fixture, id);
        }

        var page = DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt);
        var cursor = page.Cursor;
        var found = page.Jobs.Select(hint => hint.Id).ToHashSet();
        var lateId = DueWorkTestData.OrderedId(99);
        DueWorkTestData.AddDueSchedule(fixture, lateId);
        for (var index = 0; index < 12 && found.Count < originalIds.Length; index++)
        {
            page = DueWorkDiscovery.ReadPage(fixture.Database, cursor, DueWorkTestData.WakeAt);
            cursor = page.Cursor;
            found.UnionWith(page.Jobs.Select(hint => hint.Id));
            await Assert.That(found.Contains(lateId)).IsFalse();
        }
        await Assert.That(found.SetEquals(originalIds)).IsTrue();

        for (var index = 0; index < 6 && !found.Contains(lateId); index++)
        {
            page = DueWorkDiscovery.ReadPage(fixture.Database, cursor, DueWorkTestData.WakeAt);
            cursor = page.Cursor;
            found.UnionWith(page.Jobs.Select(hint => hint.Id));
        }
        await Assert.That(found.Contains(lateId)).IsTrue();
    }

    [Test]
    public async Task CancellationRejectsTheWholeDiscoveryPage()
    {
        using var fixture = new RecurringSagaDatabase();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task CursorAuthorityChangeStartsANewFixedTailSweep()
    {
        using var fixture = new RecurringSagaDatabase();
        var identity = fixture.Store.Identity;
        var upper = KeyCodec.Encode(DueWorkProtocol.ScheduleSpace, RecurringSagaDatabase.TenantId,
            RecurringSagaDatabase.DatabaseId, RecurringSagaDatabase.Domain, "partition", "jobs",
            DueWorkTestData.OrderedId(1).ToString(DueWorkFields.GuidFormat));
        var cursor = new DueSweepCursor(identity.Incarnation, identity.ReadGeneration, DueWorkKind.Saga,
            new(true, upper, upper), DuePrefixCursor.Empty);
        var matched = DueWorkCursor.Match(identity, cursor);
        await Assert.That(matched.NextPrefix).IsEqualTo(DueWorkKind.Saga);
        await Assert.That(matched.Schedules.LastKey).IsNotNull();

        var replaced = identity with { ReadGeneration = checked(identity.ReadGeneration + 1) };
        var reset = DueWorkCursor.Match(replaced, cursor);
        await Assert.That(reset.NextPrefix).IsEqualTo(DueWorkKind.Schedule);
        await Assert.That(reset.Schedules.HasUpperBound).IsFalse();
        await Assert.That(reset.Sagas.HasUpperBound).IsFalse();
    }

    private static void AddDueWork(RecurringSagaDatabase fixture, int count)
    {
        for (var index = 0; index < count; index++)
        {
            DueWorkTestData.AddDueSchedule(fixture, Guid.NewGuid());
            DueWorkTestData.AddWaitingSaga(fixture, Guid.NewGuid());
        }
    }
}
