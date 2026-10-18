using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Validated build-time application and topology selectors shared by the Aspire graph.</summary>
[ConfigurationOptions]
internal sealed class AppHostControlOptions
{
    internal TestSuiteSettings? Tests
    {
        get; set;
    }
    internal bool ProtocolCohortEnabled
    {
        get; set;
    }
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
    internal bool ProtocolCohortConfigured
    {
        get; set;
    }
    internal bool ComparisonSelectorsPresent
    {
        get; set;
    }
    internal bool ContainerFaults
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
