using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[ConfigurationOptions]
internal sealed class BenchmarkSessionCallerOptions
{
    private const string SessionKey = "KEYLOAD_BENCHMARK_SESSION_ID";
    private const string EvidenceKey = "KEYLOAD_COMPARISON_EVIDENCE_DIRECTORY";
    private const string TargetKey = "Benchmarks:Target";
    private const string NodesKey = "Benchmarks:NodeCount";
    [ConfigurationKeyName(SessionKey)]
    public string SessionId { get; set; } = string.Empty;
    [ConfigurationKeyName(EvidenceKey)]
    public string EvidenceDirectory { get; set; } = string.Empty;
    [ConfigurationKeyName(TargetKey)]
    public string Target { get; set; } = string.Empty;
    [ConfigurationKeyName(NodesKey)]
    public int NodeCount
    {
        get; set;
    }

    [ConfigurationBinding]
    internal static IOptions<BenchmarkSessionCallerOptions> Read(IConfiguration configuration)
    {
        var options = new OptionsManager<BenchmarkSessionCallerOptions>(new OptionsFactory<BenchmarkSessionCallerOptions>(
            [new ConfigureFromConfigurationOptions<BenchmarkSessionCallerOptions>(configuration)], [],
            [new ValidateOptions<BenchmarkSessionCallerOptions>(Options.DefaultName, value =>
                value.SessionId.Length > 0 && value.SessionId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
                && Path.IsPathFullyQualified(value.EvidenceDirectory), SessionKey)]));
        _ = options.Value;
        return options;
    }
}
