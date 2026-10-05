using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using ManagedCode.TimeSeries.Summers;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimeSeriesWorkloadTests
{
    private const string ExpectedHash = "d67ed223ef6c701daaa06481aaeb83dd069133cb9fd7b7d017e380d83ef0d869";

    [Test]
    public async Task DatasetHasStableHashAndOutOfOrderSamplesWithExactRangeEdges()
    {
        var first = TimeSeriesComparisonWorkloadFactory.Create("stable-run");
        var second = TimeSeriesComparisonWorkloadFactory.Create("another-run");

        await Assert.That(first.ContentHash).IsEqualTo(ExpectedHash);
        await Assert.That(second.ContentHash).IsEqualTo(ExpectedHash);
        await Assert.That(first.Samples.Length).IsEqualTo(48);
        await Assert.That(first.Samples.All(sample => sample.TagsJson == "{\"source\":\"benchmark\",\"unit\":\"value\"}")).IsTrue();
        await Assert.That(first.Samples[0].Timestamp).IsGreaterThan(first.Samples[^1].Timestamp);
        await Assert.That(first.ReadRanges.Select(range => range.Name)).IsEquivalentTo(
            ["inclusive-full-range", "inclusive-boundary", "offset-normalized", "empty-range", "invalid-range"],
            CollectionOrdering.Matching);
        await Assert.That(first.ReadRanges[1].From).IsEqualTo(first.ReadRanges[1].Until);
        await Assert.That(first.ReadRanges[2].From.ToUniversalTime()).IsEqualTo(first.ReadRanges[1].From);
        await Assert.That(first.ReadRanges[3].ExpectedCount).IsEqualTo(0);
        await Assert.That(first.ReadRanges[4].ExpectedErrorCode).IsEqualTo("BudgetExceeded");
    }

    [Test]
    public async Task BucketOracleMatchesOutOfOrderLibraryInputAndInclusiveBucketBoundaries()
    {
        var workload = TimeSeriesComparisonWorkloadFactory.Create("oracle-run");
        var reordered = workload with { Samples = [.. workload.Samples.Reverse()] };
        var actual = ManagedCodeTimeSeriesAggregation.Aggregate(reordered);
        var expected = Oracle(reordered.Samples, workload.BucketWidth);

        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(actual.Length).IsEqualTo(12);
        await Assert.That(actual[0].TimestampUtc)
            .IsEqualTo(DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        await Assert.That(actual[^1].TimestampUtc)
            .IsEqualTo(DateTimeOffset.Parse("2026-01-01T00:55:00Z", CultureInfo.InvariantCulture));
        await Assert.That(actual[0].Sum).IsEqualTo(2.5);
        await Assert.That(actual[^1].Sum).IsEqualTo(442.5);
    }

    [Test]
    public async Task ReportUsesTheLoadedPublishedLibraryPackageVersion()
    {
        var assembly = typeof(DoubleTimeSeriesSummer).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        await Assert.That(string.IsNullOrWhiteSpace(informationalVersion)).IsFalse();

        var loadedVersion = informationalVersion
            ?? throw new InvalidOperationException("The loaded TimeSeries assembly has no informational version.");
        var separator = loadedVersion.IndexOf('+', StringComparison.Ordinal);
        var packageVersion = separator < 0 ? loadedVersion : loadedVersion[..separator];
        await Assert.That(packageVersion).IsEqualTo("10.1.1");

        var workload = TimeSeriesComparisonWorkloadFactory.Create("package-version-run");
        var recorder = new TimeSeriesComparisonRecorder(workload, TimeProvider.System);
        recorder.AddLibraryTarget();
        var library = recorder.CreateReport("0123456789abcdef0123456789abcdef01234567")
            .Targets.Single(target => target.Name == "ManagedCode.TimeSeries");

        await Assert.That(library.PackageVersion).IsEqualTo(packageVersion);
        await Assert.That(library.PackageVersion).IsEqualTo("10.1.1");
    }

    private static ImmutableArray<TimeSeriesBucketValue> Oracle(
        ImmutableArray<TimeSeriesSamplePoint> samples, TimeSpan bucketWidth)
        => [.. samples.GroupBy(sample => BucketStart(sample.Timestamp, bucketWidth))
            .OrderBy(group => group.Key)
            .Select(group => new TimeSeriesBucketValue(group.Key, group.Sum(sample => sample.Value)))];

    private static DateTimeOffset BucketStart(DateTimeOffset timestamp, TimeSpan width)
    {
        var utc = timestamp.ToUniversalTime();
        var ticks = utc.UtcTicks / width.Ticks * width.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
