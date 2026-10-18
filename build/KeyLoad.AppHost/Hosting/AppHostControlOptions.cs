namespace KeyLoad.AppHost.Hosting;

/// <summary>Validated build-time application and topology selectors shared by the Aspire graph.</summary>
[ConfigurationOptions]
internal sealed class AppHostControlOptions
{
    internal bool Ephemeral
    {
        get; set;
    }
    internal bool BenchmarksEnabled
    {
        get; set;
    }
    internal bool TargetSelected
    {
        get; set;
    }
    internal bool ComparisonSelectorsPresent
    {
        get; set;
    }
    internal bool ProcessFaults
    {
        get; set;
    }
    internal bool FunctionalColdReopen
    {
        get; set;
    }
    internal int? FunctionalNodeCount
    {
        get; set;
    }
    internal bool ExclusiveOwnerContention
    {
        get; set;
    }
    internal bool LoggerModelControl
    {
        get; set;
    }
}
