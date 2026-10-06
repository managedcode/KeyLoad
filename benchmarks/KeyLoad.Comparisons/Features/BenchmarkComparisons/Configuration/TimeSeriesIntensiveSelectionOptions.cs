namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

/// <summary>Reads the actual nested intensive selector section with the conflicting root selectors.</summary>
[ConfigurationOptions]
internal sealed class TimeSeriesIntensiveSelectionOptions
{
    internal const string SectionName = "Benchmarks";
    public string? Profile { get; set; }
    public string? Target { get; set; }
    public string? NodeCount { get; set; }
    public string? Scenario { get; set; }
    public TimeSeriesIntensiveCellOptions TimeSeries { get; set; } = new();
}

/// <summary>Holds trusted configuration values before the strict intensive cell parser runs.</summary>
[ConfigurationOptions]
internal sealed class TimeSeriesIntensiveCellOptions
{
    public string? Target { get; set; }
    public string? NodeCount { get; set; }
    public string? Phase { get; set; }
    public string? Scenario { get; set; }
    public string? EvidenceProfile { get; set; }
}
