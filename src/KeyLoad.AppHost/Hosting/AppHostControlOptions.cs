using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Validated build-time application and topology selectors shared by the Aspire graph.</summary>
[ConfigurationOptions]
internal sealed class AppHostControlOptions
{
    internal TestSuiteSettings? Tests { get; set; }
    internal RequestCqrsProbeProfileSettings? RequestProbe { get; set; }
    internal bool RemoteDocumentReads { get; set; }
    internal bool RemotePartitionQueries { get; set; }
    internal bool ProtectedDocumentMovement { get; set; }
    internal int? MovementMaxBatchBytes { get; set; }
    internal int? MovementMaxFrameBytes { get; set; }
    internal bool TwoRf3 { get; set; }
    internal bool ProtocolCohortEnabled { get; set; }
    internal bool Ephemeral { get; set; }
    internal bool BenchmarksEnabled { get; set; }
    internal bool TargetSelected { get; set; }
    internal bool ProtocolCohortConfigured { get; set; }
    internal bool ComparisonSelectorsPresent { get; set; }
    internal bool ScaleSelected { get; set; }
    internal bool LoggerModelControl { get; set; }
}
