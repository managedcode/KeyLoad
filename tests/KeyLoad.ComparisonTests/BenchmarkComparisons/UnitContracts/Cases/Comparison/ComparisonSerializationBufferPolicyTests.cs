using System.Globalization;
using System.Text;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonSerializationBufferPolicyTests
{
    private const string DigestDomain = "reference-test";

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(256)]
    public async Task ConfiguredDigestStackReservationPreservesIndependentWholeStringFramesAsync(int stackBytes)
    {
        var options = Bind(nameof(SerializationExecutionOptions.DigestUtf8StackBytes), stackBytes);
        using var actual = new TimeSeriesIntensiveDigestWriter(DigestDomain, options);
        using var reference = new TimeSeriesIntensiveReferenceFramer(DigestDomain);
        foreach (var text in new[] { string.Empty, "x", "é", "€", new string('é', 257) + "😀界" })
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

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(257)]
    public async Task InvalidStandaloneDigestReservationRejectsBeforeNativeHashOwnershipAsync(int stackBytes)
    {
        var options = Options.Create(new SerializationExecutionOptions { DigestUtf8StackBytes = stackBytes });
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = new TimeSeriesIntensiveDigestWriter(DigestDomain, options));
        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(SerializationExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([SerializationExecutionOptions.ValidationMessage]);
    }

    private static IOptions<SerializationExecutionOptions> Bind(string property, int value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>(property, value.ToString(CultureInfo.InvariantCulture))]).Build();
        using var lifetime = configuration as IDisposable;
        return SerializationExecutionRegistration.Bind(configuration);
    }
}
