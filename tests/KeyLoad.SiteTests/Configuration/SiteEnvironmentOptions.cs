namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[KeyLoad.ConfigurationOptions]
internal sealed class SiteEnvironmentOptions
{
    public string? Repository { get; set; }
    public string? Browser { get; set; }
    public string? PipeDirectory { get; set; }
    public string? CoverageMode { get; set; }
    public string? BenchmarkMode { get; set; }
    public string? CoverageRoot { get; set; }
    public string? SourceRevision { get; set; }
    public string? ControlRevision { get; set; }
    public string? Aggregate { get; set; }
}
