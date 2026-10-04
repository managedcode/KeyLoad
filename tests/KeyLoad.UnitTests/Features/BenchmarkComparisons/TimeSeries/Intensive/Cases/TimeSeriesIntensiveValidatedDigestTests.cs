using System.Collections.Immutable;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveValidatedDigestTests
{
    private const string JsonbTags = "{\"kind\": \"intensive\", \"revision\": 1}";

    [Test]
    public async Task AcTsi002CombinedRawAndLatestMatchIndependentCanonicalFrames()
    {
        var expected = TimeSeriesIntensiveReferenceOracle.Raw(0);
        var actual = expected.Select(row => row with { TagsJson = JsonbTags }).ToImmutableArray();
        await Assert.That(TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Raw(expected));
        await Assert.That(TimeSeriesIntensiveResultDigest.ValidatedLatest(expected[0], actual[0])).IsEqualTo(TimeSeriesIntensiveReferenceResults.Latest(expected[0]));
        await Assert.That(TimeSeriesIntensiveResultDigest.ValidatedLatest(null, null)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Latest(null));
        await Assert.That(TimeSeriesIntensiveResultDigest.ValidatedRaw([], [])).IsEqualTo(TimeSeriesIntensiveReferenceResults.Raw([]));
    }

    [Test]
    public async Task AcTsi002CombinedPassRejectsCorruptionAfterRepeatedMemoHits()
    {
        var expected = TimeSeriesIntensiveReferenceOracle.Raw(0);
        var actual = expected.Select(row => row with { TagsJson = JsonbTags }).ToImmutableArray();
        foreach (var invalid in new[] { "{", "{}", "{\"kind\":\"other\",\"revision\":1}", "{\"kind\":\"intensive\",\"revision\":2}",
            "{\"kind\":\"intensive\",\"revision\":1,\"extra\":true}" })
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected,
                actual.SetItem(500, actual[500] with { TagsJson = invalid })));
        }

        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected,
                actual.SetItem(500, actual[500] with { Sample = actual[500].Sample with { Value = invalid } })));
        }

        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, default));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual.RemoveAt(0)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual.Add(actual[0])));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual.SetItem(0, actual[1]).SetItem(1, actual[0])));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual.SetItem(500, null!)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedLatest(expected[0], null));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedLatest(null, actual[0]));
        await Assert.That(actual.Length).IsEqualTo(516);
    }

    [Test]
    public async Task AcTsi008RepeatedJsonbTagsHaveBoundedSynchronousValidationAllocation()
    {
        var expected = TimeSeriesIntensiveReferenceOracle.Raw(0);
        var actual = expected.Select(row => row with { TagsJson = JsonbTags }).ToImmutableArray();
        var oneExpected = ImmutableArray.Create(expected[0]);
        var oneActual = ImmutableArray.Create(actual[0]);
        _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual);
        _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(oneExpected, oneActual);
        var small = Allocation(oneExpected, oneActual);
        var large = Allocation(expected, actual);
        await Assert.That(large).IsLessThan(small + 32768);
    }

    [Test]
    public async Task AcTsi002CombinedPassChecksEveryIdentityFieldAfterMemoHit()
    {
        var expected = TimeSeriesIntensiveReferenceOracle.Raw(0);
        var actual = expected.Select(row => row with { TagsJson = JsonbTags }).ToImmutableArray();
        var row = actual[500];
        foreach (var corrupt in new[]
        {
            row with { SeriesId = "foreign" },
            row with { Sequence = row.Sequence + 1 },
            row with { Sample = row.Sample with { EventId = "foreign" } },
            row with { Sample = row.Sample with { Timestamp = row.Sample.Timestamp.AddTicks(1) } },
            row with { Sample = row.Sample with { Value = row.Sample.Value + 0.25 } },
            row with { Sample = null! },
            row with { TagsJson = null! }
        })
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual.SetItem(500, corrupt)));
        }

        await Assert.That(TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Raw(expected));
    }

    private static long Allocation(ImmutableArray<SampleRecord> expected, ImmutableArray<SampleRecord> actual)
    {
        var start = GC.GetAllocatedBytesForCurrentThread();
        _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual);
        return GC.GetAllocatedBytesForCurrentThread() - start;
    }
}
