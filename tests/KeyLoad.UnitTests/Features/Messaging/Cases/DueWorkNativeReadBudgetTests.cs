using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueWorkNativeReadBudgetTests
{
    [Test]
    public async Task FixedTailAndForwardScanChargeExactNativeBytesAndRejectOneByteLess()
    {
        long expectedBytes;
        using (var measurement = CreateFixture())
        {
            DueWorkTestData.PutSchedule(measurement, ScheduleId, ScheduleId, DueWorkTestData.EmptyJson);
            expectedBytes = MeasureSingleScheduleRead(measurement);
        }

        using var exact = CreateFixture(expectedBytes);
        DueWorkTestData.PutSchedule(exact, ScheduleId, ScheduleId, DueWorkTestData.EmptyJson);
        var page = DueWorkDiscovery.ReadPage(exact.Database, null, DueWorkTestData.WakeAt);
        await Assert.That(page.ExaminedRecords).IsEqualTo(2);
        await Assert.That(page.ExaminedBytes).IsEqualTo(expectedBytes);
        await Assert.That(page.AdmittedValueBytes).IsLessThan(expectedBytes);

        using var excess = CreateFixture(expectedBytes - 1);
        DueWorkTestData.PutSchedule(excess, ScheduleId, ScheduleId, DueWorkTestData.EmptyJson);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            DueWorkDiscovery.ReadPage(excess.Database, null, DueWorkTestData.WakeAt));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static long MeasureSingleScheduleRead(RecurringSagaDatabase fixture)
    {
        var key = KeySpace.Partition(DueWorkProtocol.ScheduleSpace, fixture.Partition, fixture.Queue.Queue,
            ScheduleId.ToString(DueWorkFields.GuidFormat));
        long observedValueBytes = -1;
        var found = fixture.Store.Read(view => view.ReadValue(key, value => observedValueBytes = value.Length));
        var valueBytes = found ? observedValueBytes : -1;
        if (valueBytes < 0)
        {
            throw new InvalidOperationException(MissingScheduleRecord);
        }
        return checked(2 * checked(key.LongLength + valueBytes));
    }

    private static RecurringSagaDatabase CreateFixture() => new();

    private static RecurringSagaDatabase CreateFixture(long maxQueryReadBytes)
        => new(new() { MaxQueryReadBytes = maxQueryReadBytes });

    private const string MissingScheduleRecord = "The independently seeded schedule record is missing.";
    private static readonly Guid ScheduleId = Guid.ParseExact("00000000000000000000000000000001", DueWorkFields.GuidFormat);
}
