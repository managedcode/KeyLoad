using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

public sealed class DueWorkPageBudgetTests
{
    [Test]
    public async Task ValidValueCrossingAdmittedByteCapIsRevisitedAfterPrefixAlternation()
    {
        using var fixture = new RecurringSagaDatabase(new() { MaxBatchBytes = SmallBatchLimit });
        var first = DueWorkTestData.OrderedId(1);
        var second = DueWorkTestData.OrderedId(2);
        var firstBytes = DueWorkTestData.PutSchedule(fixture, first, first,
            DueWorkTestData.LargeJson(AdmittedPayloadCharacters));
        var secondBytes = DueWorkTestData.PutSchedule(fixture, second, second,
            DueWorkTestData.LargeJson(AdmittedPayloadCharacters));
        await Assert.That(firstBytes < SmallBatchLimit && secondBytes < SmallBatchLimit).IsTrue();
        await Assert.That(firstBytes + secondBytes > SmallBatchLimit).IsTrue();

        var firstPage = DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt);
        await Assert.That(firstPage.Jobs.Select(hint => hint.Id).ToArray()).IsEquivalentTo(new[] { first });
        await Assert.That(firstPage.AdmittedValueBytes).IsEqualTo(firstBytes);
        await Assert.That(firstPage.ExaminedBytes > firstPage.AdmittedValueBytes).IsTrue();
        await Assert.That(firstPage.Cursor.Schedules.LastKey).IsNotNull();
        await Assert.That(firstPage.Cursor.Sagas.HasUpperBound).IsFalse();

        var secondPage = DueWorkDiscovery.ReadPage(fixture.Database, firstPage.Cursor, DueWorkTestData.WakeAt);
        await Assert.That(secondPage.ScannedPrefix).IsEqualTo(DueWorkKind.Saga);
        var thirdPage = DueWorkDiscovery.ReadPage(fixture.Database, secondPage.Cursor, DueWorkTestData.WakeAt);
        await Assert.That(thirdPage.Jobs.Select(hint => hint.Id).ToArray()).IsEquivalentTo(new[] { second });
        await Assert.That(thirdPage.AdmittedValueBytes).IsEqualTo(secondBytes);
    }

    [Test]
    public async Task IndividuallyOversizedValueIsRejectedAndItsCanonicalKeyAdvances()
    {
        using var fixture = new RecurringSagaDatabase(new() { MaxBatchBytes = SmallBatchLimit });
        var oversized = DueWorkTestData.OrderedId(1);
        var following = DueWorkTestData.OrderedId(2);
        var size = DueWorkTestData.PutSchedule(fixture, oversized, oversized,
            DueWorkTestData.LargeJson(OversizedPayloadCharacters));
        _ = DueWorkTestData.PutSchedule(fixture, following, following, DueWorkTestData.EmptyJson);
        await Assert.That(size > SmallBatchLimit).IsTrue();

        var first = DueWorkDiscovery.ReadPage(fixture.Database, null, DueWorkTestData.WakeAt);
        await Assert.That(first.Jobs.IsEmpty).IsTrue();
        await Assert.That(first.Rejected.Length).IsEqualTo(1);
        await Assert.That(first.Rejected[0].Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(first.AdmittedValueBytes).IsEqualTo(0);
        await Assert.That(first.Cursor.Schedules.LastKey).IsNotNull();
        var nextPrefix = DueWorkDiscovery.ReadPage(fixture.Database, first.Cursor, DueWorkTestData.WakeAt);
        var nextSchedule = DueWorkDiscovery.ReadPage(fixture.Database, nextPrefix.Cursor, DueWorkTestData.WakeAt);
        await Assert.That(nextSchedule.Jobs.Select(hint => hint.Id).ToArray()).IsEquivalentTo(new[] { following });
        await Assert.That(nextSchedule.Rejected.IsEmpty).IsTrue();
    }

    private const int SmallBatchLimit = 4_096;
    private const int AdmittedPayloadCharacters = 2_500;
    private const int OversizedPayloadCharacters = 5_000;
}
