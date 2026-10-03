using System.ComponentModel;
using System.Text.Json;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonTopologyTests
{
    private const string TopologySetting = "Benchmarks:Topology";
    private const string BenchmarkSection = "Benchmarks";

    [Test]
    public async Task AcBct003DefaultAndLegacyConfigurationSpellingsBindThroughRealConfiguration()
    {
        using var configuration = new ConfigurationManager();
        await Assert.That(ComparisonOptions.Read(configuration).Topology).IsEqualTo(ComparisonTopology.Standalone);

        foreach (var (configured, expected) in new[]
        {
            ("Single", ComparisonTopology.Standalone),
            (" single ", ComparisonTopology.Standalone),
            ("Replicated", ComparisonTopology.Replicated),
            ("replicated", ComparisonTopology.Replicated),
            ("0", ComparisonTopology.Standalone),
            ("1", ComparisonTopology.Replicated),
            ("Single, Replicated", ComparisonTopology.Replicated)
        })
        {
            configuration[TopologySetting] = configured;
            var options = ComparisonOptions.Read(configuration);
            await Assert.That(options.Topology).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task AcBct003JsonKeepsSingleAndReplicatedNamesAndTheirNumericValues()
    {
        await Assert.That(JsonSerializer.Serialize(ComparisonTopology.Standalone, ReportWriter.JsonOptions)).IsEqualTo("\"Single\"");
        await Assert.That(JsonSerializer.Serialize(ComparisonTopology.Replicated, ReportWriter.JsonOptions)).IsEqualTo("\"Replicated\"");
        var standalone = Enum.Parse<ComparisonTopology>("Standalone");
        var replicated = Enum.Parse<ComparisonTopology>("Replicated");
        await Assert.That((int)standalone).IsEqualTo(0);
        await Assert.That((int)replicated).IsEqualTo(1);
        await Assert.That(JsonSerializer.Deserialize<ComparisonTopology>("\"Single\"", ReportWriter.JsonOptions))
            .IsEqualTo(ComparisonTopology.Standalone);
    }

    [Test]
    public async Task AcBct003TypeDescriptorPreservesLegacyConfigurationOutputNames()
    {
        var converter = TypeDescriptor.GetConverter(typeof(ComparisonTopology));
        await Assert.That(converter).IsTypeOf<ComparisonTopologyConverter>();
        await Assert.That(converter.ConvertToInvariantString(ComparisonTopology.Standalone)).IsEqualTo("Single");
        await Assert.That(converter.ConvertToInvariantString(ComparisonTopology.Replicated)).IsEqualTo("Replicated");
    }

    [Test]
    public void AcBct003MalformedAndUndefinedConfigurationRemainsRejectedByBinderOrValidation()
    {
        using var configuration = new ConfigurationManager();
        configuration[TopologySetting] = "Single, 3";
        var combined = configuration.GetSection(BenchmarkSection).Get<ComparisonOptions>()!;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(combined.Validate);

        foreach (var configured in new[] { "", "unknown" })
        {
            configuration[TopologySetting] = configured;
            Assert.ThrowsExactly<InvalidOperationException>(() => ComparisonOptions.Read(configuration));
        }

        foreach (var configured in new[] { "3", "4" })
        {
            configuration[TopologySetting] = configured;
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ComparisonOptions.Read(configuration));
        }
    }
}
