using Microsoft.Extensions.Configuration;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

/// <summary>Selects the client-owned load lifecycle and its disposable storage root.</summary>
[ConfigurationOptions]
internal sealed class LoadTestsStartupOptions
{
    private const string EnabledKey = "Benchmarks:Enabled";
    private const string OutputKey = "Benchmarks:Output";
    private const string RootKey = "Benchmarks:DataRoot";
    [ConfigurationKeyName(EnabledKey)]
    public bool BenchmarkMode { get; set; }
    [ConfigurationKeyName(OutputKey)]
    public string? BenchmarkOutput { get; set; }
    [ConfigurationKeyName(RootKey)]
    public string? BenchmarkRoot { get; set; }

}
