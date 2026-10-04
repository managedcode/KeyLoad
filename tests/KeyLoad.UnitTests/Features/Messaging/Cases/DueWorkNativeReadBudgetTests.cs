using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

public sealed class DueWorkNativeReadBudgetTests
{
    [Test]
    public async Task FixedTailAndForwardScanChargeExactNativeBytesAndRejectOneByteLess()
    {
        var expectedBytes = MeasureSingleScheduleRead();
        using var exact = CreateFixture(expectedBytes);
        DueWorkTestData.PutSchedule(exact, ScheduleId, ScheduleId, DueWorkTestData.EmptyJson);
        var page = DueWorkDiscovery.ReadPage(exact.Database, null, DueWorkTestData.WakeAt);
        await Assert.That(page.ExaminedBytes).IsEqualTo(expectedBytes);
        await Assert.That(page.AdmittedValueBytes).IsLessThan(expectedBytes);

        using var excess = CreateFixture(expectedBytes - 1);
        DueWorkTestData.PutSchedule(excess, ScheduleId, ScheduleId, DueWorkTestData.EmptyJson);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            DueWorkDiscovery.ReadPage(excess.Database, null, DueWorkTestData.WakeAt));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static long MeasureSingleScheduleRead()
    {
        var lane = new QueueLaneRef(new(RecurringSagaDatabase.TenantId, RecurringSagaDatabase.DatabaseId,
            RecurringSagaDatabase.Domain, PartitionId), RecurringSagaDatabase.QueueName);
        var definition = DueWorkTestData.Definition(lane, ScheduleId, RecurringSagaDatabase.Epoch.AddSeconds(-1));
        var record = new RecurringScheduleRecord(lane, ScheduleId, RecurringSagaDatabase.RootPrincipal,
            definition, 1, 1, 0, false);
        var valueBytes = NativeSerialization.Serialize(record).LongLength;
        var keyBytes = KeySpace.Partition(DueWorkProtocol.ScheduleSpace, lane.Partition, lane.Queue,
            ScheduleId.ToString(DueWorkFields.GuidFormat)).LongLength;
        return checked(2 * checked(keyBytes + valueBytes));
    }

    private static RecurringSagaDatabase CreateFixture(long maxQueryReadBytes)
        => new(new() { MaxQueryReadBytes = maxQueryReadBytes });

    private const string PartitionId = "orders-1";
    private static readonly Guid ScheduleId = Guid.ParseExact("00000000000000000000000000000001", DueWorkFields.GuidFormat);
}
