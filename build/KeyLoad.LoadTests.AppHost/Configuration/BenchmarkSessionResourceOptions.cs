using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Selects one live execution database for the complete measured session.</summary>
[ConfigurationOptions]
internal sealed class BenchmarkSessionResourceOptions
{
    internal const string Section = "Benchmarks";
    internal const string Profile = BenchmarkSessionContract.Profile;
    private const string BenchmarkSettingPrefix = "Benchmarks:";
    private const int ReplicatedNodes = 3;
    public bool Session
    {
        get; set;
    }
    public string? Target
    {
        get; set;
    }
    public int NodeCount
    {
        get; set;
    }
    public string? EvidenceProfile
    {
        get; set;
    }
    public string? SessionId
    {
        get; set;
    }
    /// <summary>Release-published KeyLoad server image reference pinned to the digest resolved for this run.</summary>
    public string? KeyLoadImage
    {
        get; set;
    }

    [ConfigurationBinding]
    internal static IOptions<BenchmarkSessionResourceOptions> Read(IConfiguration configuration)
    {
        var options = new OptionsManager<BenchmarkSessionResourceOptions>(new OptionsFactory<BenchmarkSessionResourceOptions>(
            [new ConfigureFromConfigurationOptions<BenchmarkSessionResourceOptions>(configuration.GetSection(Section))], [],
            [new ValidateOptions<BenchmarkSessionResourceOptions>(Options.DefaultName, value => !value.Session
                || value.EvidenceProfile == Profile && !string.IsNullOrWhiteSpace(value.SessionId)
                    && BenchmarkSessionContract.Targets.Contains(value.Target!)
                    && value.NodeCount is 1 or ReplicatedNodes
                    && !BenchmarkSessionContract.ForbiddenOverrides.Any(name =>
                        !string.IsNullOrEmpty(configuration[BenchmarkSettingPrefix + name])), Profile)]));
        _ = options.Value;
        return options;
    }
}
