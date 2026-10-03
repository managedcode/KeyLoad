using System.Text.Json;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveTagScopeTests
{
    private const string JsonbTags = "{\"kind\": \"intensive\", \"revision\": 1}";

    [Test]
    public async Task AcTsi002SuccessfulMemoSurvivesChangedExtraAndMalformedTags()
    {
        var scope = new TimeSeriesIntensiveTagScope();
        await Assert.That(scope.Canonical(JsonbTags)).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Tags);
        await Assert.That(scope.Canonical("{\"kind\":\"other\",\"revision\":1}")).IsNotEqualTo(TimeSeriesIntensiveReferenceOracle.Tags);
        await Assert.That(scope.Canonical("{\"kind\":\"intensive\",\"revision\":1,\"extra\":true}")).IsNotEqualTo(TimeSeriesIntensiveReferenceOracle.Tags);
        Assert.Throws<JsonException>(() => scope.Canonical("{"));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 516; index++)
        {
            _ = scope.Canonical(JsonbTags);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsLessThan(1024L);
    }

    [Test]
    public async Task AcTsi002AlternatingEquivalentSpellingsAndEscapesRetainCanonicalMeaning()
    {
        var scope = new TimeSeriesIntensiveTagScope();
        foreach (var spelling in new[] { JsonbTags, "{ \"revision\":1,\"kind\":\"intensive\" }", JsonbTags,
            "{\"kind\":\"int\\u0065nsive\",\"revision\":1}" })
        {
            await Assert.That(scope.Canonical(spelling)).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Tags);
        }
    }
}
