using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkOrchestration;

[ConfigurationOptions]
internal sealed class BenchmarkSessionCallerOptions
{
    private const int EmptyCount = 0;
    private const char DashSeparator = '-';

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
        var options = new OptionsManager<BenchmarkSessionCallerOptions>(new CallerOptionsFactory(configuration));
        _ = options.Value;
        return options;
    }

    [ConfigurationBinding]
    private sealed class CallerOptionsFactory(IConfiguration configuration)
        : OptionsFactory<BenchmarkSessionCallerOptions>(
            [new ConfigureFromConfigurationOptions<BenchmarkSessionCallerOptions>(configuration)], [],
            [new ValidateOptions<BenchmarkSessionCallerOptions>(Options.DefaultName, value =>
                value.SessionId.Length > EmptyCount && value.SessionId.All(character => char.IsAsciiLetterOrDigit(character) || character == DashSeparator)
                && Path.IsPathFullyQualified(value.EvidenceDirectory), SessionKey)])
    {
        protected override BenchmarkSessionCallerOptions CreateInstance(string name) => new();
    }
}
