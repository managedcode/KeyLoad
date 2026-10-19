using System.Text;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonSerializationBufferPolicyTests
{
    private const string DigestDomain = "reference-test";

    [Test]
    public async Task DigestPreservesIndependentWholeStringFramesAsync()
    {
        using var actual = new TimeSeriesIntensiveDigestWriter(DigestDomain);
        using var reference = new TimeSeriesIntensiveReferenceFramer(DigestDomain);
        foreach (var text in new[] { string.Empty, "x", "é", "€", new string('x', 256), new string('x', 257), new string('é', 257) + "😀界" })
        {
            actual.String(text);
            reference.Text(text);
        }
        actual.Field("negative", long.MinValue);
        reference.Field("negative", long.MinValue);
        actual.Optional("present", 0);
        reference.Optional("present", 0);
        actual.Optional("absent", null);
        reference.Optional("absent", null);
        Assert.ThrowsExactly<EncoderFallbackException>(() => actual.String("\uD800"));
        await Assert.That(actual.Finish()).IsEqualTo(reference.Finish());
    }
}
