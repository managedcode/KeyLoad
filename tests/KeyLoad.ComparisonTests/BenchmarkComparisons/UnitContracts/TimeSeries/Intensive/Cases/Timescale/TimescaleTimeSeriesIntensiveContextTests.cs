using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveContextTests
{
    [Test]
    public async Task AcTsi008ContextBindsOneRunToValidatedSchemaAndProfileSet()
    {
        var first = new TimescaleTimeSeriesIntensiveContext("timescale-context-run");
        var second = new TimescaleTimeSeriesIntensiveContext("timescale-context-run");
        await Assert.That(first.SchemaName).IsEqualTo(second.SchemaName);
        await Assert.That(first.SeriesSet).IsEqualTo(TimeSeriesIntensiveProfile.Name);
        await Assert.That(first.OwnerId).IsNotEqualTo(Guid.Empty);
        await Assert.That(first.OwnerId).IsNotEqualTo(second.OwnerId);
    }

    [Test]
    public async Task AcTsi008ContextRejectsMissingRunIdentityBeforeProviderAllocation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => { _ = new TimescaleTimeSeriesIntensiveContext(" "); });
        await Assert.That(TimeSeriesIntensiveProfile.Name.Length).IsGreaterThan(0);
    }
}
