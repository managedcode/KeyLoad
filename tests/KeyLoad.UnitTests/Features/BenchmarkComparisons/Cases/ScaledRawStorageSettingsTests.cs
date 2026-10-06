using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Locks ZoneTree's retained-corpus capacity model and qualification headroom.</summary>
internal sealed class ScaledRawStorageSettingsTests
{
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int HundredThousand = 100_000;
    private const int OneMillion = 1_000_000;
    private const int FiveMillion = 5_000_000;
    private const int InvalidPayloadBytes = 64;
    private const long ExpectedHeadroomBytes = 2_147_483_648;

    [Test]
    [Arguments(HundredThousand, SmallPayloadBytes)]
    [Arguments(HundredThousand, LargePayloadBytes)]
    [Arguments(OneMillion, SmallPayloadBytes)]
    [Arguments(OneMillion, LargePayloadBytes)]
    public async Task AcScale003CapacityIncludesTheCompleteRetainedZoneTreeCorpusAndHeadroom(
        int recordCount, int payloadBytes)
    {
        var expectedKeysAndOrder = ((long)recordCount + 1L) * 16L + (long)recordCount * sizeof(int);
        var expectedValues = (long)recordCount * payloadBytes;
        var expectedScratch = (long)payloadBytes * 3L;
        var expectedCapacity = expectedKeysAndOrder + expectedValues + expectedScratch
            + ExpectedHeadroomBytes;

        await Assert.That(ScaledRawStorageSettings.CapacityBound(recordCount, payloadBytes, UnitBenchmarkOptions.ScaledStorage().Value.RequiredHeadroomBytes))
            .IsEqualTo(expectedCapacity);
        await Assert.That(expectedCapacity - expectedKeysAndOrder - expectedValues - expectedScratch)
            .IsEqualTo(ExpectedHeadroomBytes);
    }

    [Test]
    public async Task AcScale003CapacityRejectsUnsupportedSizeAndPayloadBeforeStorageOpen()
    {
        await Assert.That(() => ValidateCapacity(0, SmallPayloadBytes)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ValidateCapacity(OneMillion + 1, SmallPayloadBytes))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ValidateCapacity(FiveMillion, SmallPayloadBytes))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ValidateCapacity(HundredThousand, InvalidPayloadBytes))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static void ValidateCapacity(int recordCount, int payloadBytes)
        => ScaledRawStorageSettings.ValidateFixtureCapacity(recordCount, payloadBytes, UnitBenchmarkOptions.ScaledPreparation.Value);
}
