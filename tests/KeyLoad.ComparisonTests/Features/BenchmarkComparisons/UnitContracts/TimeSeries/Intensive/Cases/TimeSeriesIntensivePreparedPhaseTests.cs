using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePreparedPhaseTests
{
    [Test]
    public async Task AcTsi002PreparedMeasuredCommandIdsMatchEveryIndependentPurpose()
    {
        var phase = TimeSeriesIntensivePreparedPhase.Create(TimeSeriesIntensiveScenario.Append, "phase-reference", 2, false);
        await Assert.That(phase.SeriesId).IsEqualTo("measured-r2");
        await Assert.That(phase.Count).IsEqualTo(10000);
        await Assert.That(phase.Commands.Length).IsEqualTo(10000);
        for (var index = 0; index < 10000; index++)
        {
            await Assert.That(phase.Commands[index]).IsEqualTo(TimeSeriesIntensiveReferenceOracle.CommandId("phase-reference", 2, index));
        }
    }

    [Test]
    public async Task AcTsi004ReadPhasesReuseSeedWithoutAllocatingAppendReceiptState()
    {
        var phase = TimeSeriesIntensivePreparedPhase.Create(TimeSeriesIntensiveScenario.Latest, "read-reference", 4, true);
        await Assert.That(phase.SeriesId).IsEqualTo("seed");
        await Assert.That(phase.Count).IsEqualTo(256);
        await Assert.That(phase.Commands.IsEmpty).IsTrue();
        await Assert.That(phase.Appends).IsNull();
    }
}
