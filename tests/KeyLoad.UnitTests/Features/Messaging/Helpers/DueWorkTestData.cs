using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class DueWorkTestData
{
    internal const string EmptyJson = "{}";
    internal const int PageCount = 40;
    internal static readonly DateTimeOffset WakeAt = RecurringSagaDatabase.Epoch.AddMinutes(3);

    internal static RecurringScheduleDefinition Definition(QueueLaneRef lane, Guid id,
        DateTimeOffset dueAt, string payload = EmptyJson)
        => new(lane, id, dueAt, TimeSpan.FromSeconds(1), DueWorkFields.UtcZone, RecurringMisfirePolicy.CatchUp,
            payload, EmptyJson);

    internal static void AddDueSchedule(RecurringSagaDatabase fixture, Guid id, QueueLaneRef? lane = null)
    {
        var actualLane = lane ?? fixture.Queue;
        var definition = Definition(actualLane, id, RecurringSagaDatabase.Epoch.AddSeconds(-1));
        _ = fixture.Commit(actualLane.Partition, new ConfigureRecurringSchedule(definition, 0));
    }

    internal static void AddWaitingSaga(RecurringSagaDatabase fixture, Guid id, QueueLaneRef? lane = null)
    {
        var actualLane = lane ?? fixture.Queue;
        var timeout = new SagaTimeoutDefinition(fixture.TimeoutQueue, EmptyJson, EmptyJson);
        var mutation = new CompareExchangeSaga(actualLane, id, 0, SagaPhase.Waiting, EmptyJson,
            RecurringSagaDatabase.Epoch.AddMinutes(1), timeout);
        _ = fixture.Commit(actualLane.Partition, mutation);
    }

    internal static int PutSchedule(RecurringSagaDatabase fixture, Guid keyId, Guid recordId,
        string payload)
    {
        var definition = Definition(fixture.Queue, recordId, RecurringSagaDatabase.Epoch.AddSeconds(-1), payload);
        var record = new RecurringScheduleRecord(fixture.Queue, recordId, RecurringSagaDatabase.RootPrincipal,
            definition, 1, 1, 0, false);
        var key = KeySpace.Partition(DueWorkProtocol.ScheduleSpace, fixture.Partition,
            fixture.Queue.Queue, keyId.ToString(DueWorkFields.GuidFormat));
        var value = NativeSerialization.Serialize(record);
        fixture.Store.Commit(transaction =>
        {
            transaction.Put(key, value);
            return true;
        });
        return value.Length;
    }

    internal static ImmutableArray<DueWorkPage> ReadPages(RecurringSagaDatabase fixture,
        int expectedHints, out ImmutableArray<DueWorkHint> hints)
    {
        var pages = ImmutableArray.CreateBuilder<DueWorkPage>();
        var found = ImmutableArray.CreateBuilder<DueWorkHint>();
        DueSweepCursor? cursor = null;
        for (var pageIndex = 0; pageIndex < 16 && found.Count < expectedHints; pageIndex++)
        {
            var page = DueWorkDiscovery.ReadPage(fixture.Database, cursor, WakeAt);
            pages.Add(page);
            found.AddRange(page.Jobs);
            cursor = page.Cursor;
        }
        hints = [.. found];
        return [.. pages];
    }

    internal static Guid OrderedId(int suffix)
        => Guid.ParseExact(string.Concat("000000000000000000000000000000", suffix.ToString("D2")), DueWorkFields.GuidFormat);

    internal static string LargeJson(int characters)
        => string.Concat("{\"data\":\"", new string('x', characters), "\"}");
}
