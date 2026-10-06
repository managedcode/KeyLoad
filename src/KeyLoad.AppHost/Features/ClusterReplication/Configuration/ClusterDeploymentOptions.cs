using KeyLoad;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Central settings applied to each real RF3 server container.</summary>
[ConfigurationOptions]
internal sealed record ClusterDeploymentOptions
{
    private const int DefaultSnapshotThreshold = 1024;
    private const int MinimumPositive = 1;
    private const string SnapshotKey = "KeyLoad:SnapshotThreshold";
    private const string LoggingKey = "Logging:LogLevel:Default";
    private const string DefaultLoggingLevel = "Warning";
    internal const string ReplaySection = "KeyLoad:ReplayAdmission";
    internal const int VoterCount = 3;
    internal const string ValidationMessage = "Cluster deployment settings are invalid.";
    [ConfigurationKeyName(SnapshotKey)]
    public int SnapshotThreshold { get; init; } = DefaultSnapshotThreshold;
    [ConfigurationKeyName(LoggingKey)]
    public string LoggingLevel { get; init; } = DefaultLoggingLevel;
    internal bool IsValid() => SnapshotThreshold >= MinimumPositive && !string.IsNullOrWhiteSpace(LoggingLevel);
}
