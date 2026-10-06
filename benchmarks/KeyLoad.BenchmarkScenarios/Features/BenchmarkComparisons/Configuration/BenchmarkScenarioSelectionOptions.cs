namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Holds the generated runner's explicit engine and qualification-scale selectors.</summary>
[ConfigurationOptions]
internal sealed class BenchmarkScenarioSelectionOptions
{
    internal const string SectionName = "BenchmarkScenarioSelection";
    internal const string ZoneTree = "zonetree";
    internal const int HundredThousandRecords = 100_000;
    internal const int OneMillionRecords = 1_000_000;
    public string Engine { get; set; } = ZoneTree;
    public string? RecordCount { get; set; }
}
