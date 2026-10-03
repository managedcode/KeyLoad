using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveWriterTests
{
    [Test]
    public async Task AcTsi002WriterUsesUtf8ByteLengthSignedEndianAndOptionalMarkers()
    {
        var text = new string('\u00e9', 257);
        using var actual = new TimeSeriesIntensiveDigestWriter("reference-test");
        using var reference = new TimeSeriesIntensiveReferenceFramer("reference-test");
        actual.Field("unicode", text);
        reference.Field("unicode", text);
        actual.Field("negative", long.MinValue);
        reference.Field("negative", long.MinValue);
        actual.Optional("present", 0);
        reference.Optional("present", 0);
        actual.Optional("absent", null);
        reference.Optional("absent", null);
        await Assert.That(actual.Finish()).IsEqualTo(reference.Finish());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => actual.Count(-1));
        Assert.ThrowsExactly<ArgumentNullException>(() => actual.String(null!));
    }

    [Test]
    public async Task AcTsi002EmptyArraysAndNonNullWindowEndHaveUnambiguousFrames()
    {
        await Assert.That(TimeSeriesIntensiveResultDigest.Raw([])).IsEqualTo(TimeSeriesIntensiveReferenceResults.Raw([]));
        await Assert.That(TimeSeriesIntensiveResultDigest.Windows([])).IsEqualTo(TimeSeriesIntensiveReferenceResults.Windows([]));
        var from = new DateTimeOffset(639028224000000000, TimeSpan.Zero);
        ImmutableArray<SampleAggregateWindow> windows = [new(from, from.AddMinutes(1), new(0, 0, null, null, null))];
        await Assert.That(TimeSeriesIntensiveResultDigest.Windows(windows)).IsEqualTo(TimeSeriesIntensiveReferenceResults.Windows(windows));
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveResultDigest.Raw(default));
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveResultDigest.Windows(default));
    }

    [Test]
    public async Task AcTsi002CommandIdsUseFirstSixteenHashBytesInDotNetGuidConvention()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("run-a:measured:4:9999"));
        var expected = new Guid(bytes.AsSpan(0, 16));
        await Assert.That(TimeSeriesIntensivePlans.CommandId("run-a", "measured:4:9999")).IsEqualTo(expected);
    }
}
