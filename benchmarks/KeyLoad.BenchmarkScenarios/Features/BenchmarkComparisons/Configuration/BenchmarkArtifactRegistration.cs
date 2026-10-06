using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Binds exact existing artifact environment variables without inventing provenance defaults.</summary>
[ConfigurationBinding]
internal static class BenchmarkArtifactRegistration
{
    private const string SettingSeparator = ":";

    internal static IOptions<BenchmarkArtifactOptions> ReadNativeSerialization()
        => Read(new Dictionary<string, string?>
        {
            [Key(nameof(BenchmarkArtifactOptions.NativeSerializationDirectory))] =
                Environment.GetEnvironmentVariable(NativeSerializationBenchmarkManifest.DirectoryVariable)
        });

    internal static IOptions<BenchmarkArtifactOptions> ReadSampleChunk()
        => Read(new Dictionary<string, string?>
        {
            [Key(nameof(BenchmarkArtifactOptions.SampleChunkDirectory))] =
                Environment.GetEnvironmentVariable(SampleChunkBenchmarkManifest.DirectoryEnvironment),
            [Key(nameof(BenchmarkArtifactOptions.SourceHead))] =
                Environment.GetEnvironmentVariable(SampleChunkBenchmarkManifest.SourceHeadEnvironment),
            [Key(nameof(BenchmarkArtifactOptions.SourceInventorySha256))] =
                Environment.GetEnvironmentVariable(SampleChunkBenchmarkManifest.SourceInventoryEnvironment)
        });

    private static IOptions<BenchmarkArtifactOptions> Read(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var configurationLifetime = configuration as IDisposable;
        return BenchmarkScenarioOptionsRegistration.Read<BenchmarkArtifactOptions>(configuration,
            BenchmarkArtifactOptions.SectionName, settings => settings.IsValid(), BenchmarkArtifactOptions.InvalidSource);
    }

    private static string Key(string property) => BenchmarkArtifactOptions.SectionName + SettingSeparator + property;
}
