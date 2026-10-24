namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteCoverageModeSelector
{
    public static SiteCoverageMode Current
    {
        get
        {
            var value = SiteEnvironmentConfiguration.Capture().Value.BenchmarkMode;
            return Resolve(value);
        }
    }

    public static SiteCoverageMode Resolve(string? value) => value switch
    {
        null or SiteCoverageTokens.MeasuredBenchmarkMode => SiteCoverageMode.Measured,
        SiteCoverageTokens.NoBenchmarkMode => SiteCoverageMode.ContentOnly,
        _ => throw new InvalidOperationException(SiteCoverageTokens.InvalidModeFailure),
    };

    public static string[] Sources(SiteCoverageMode mode) => mode switch
    {
        SiteCoverageMode.Measured => SiteCoverageSourceInventory.ProductionSources,
        SiteCoverageMode.ContentOnly => SiteCoverageSourceInventory.ContentSources,
        _ => throw new InvalidOperationException(SiteCoverageTokens.InvalidModeFailure),
    };

    public static string[] CriticalSources(SiteCoverageMode mode) => mode switch
    {
        SiteCoverageMode.Measured => SiteCoverageSourceInventory.MeasuredCriticalSources,
        SiteCoverageMode.ContentOnly => SiteCoverageSourceInventory.ContentCriticalSources,
        _ => throw new InvalidOperationException(SiteCoverageTokens.InvalidModeFailure),
    };
}
