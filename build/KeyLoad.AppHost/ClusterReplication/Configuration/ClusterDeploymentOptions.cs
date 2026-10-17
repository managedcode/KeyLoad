using System.Net;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Independent Orleans membership and physical replica deployment settings.</summary>
[ConfigurationOptions]
internal sealed record ClusterDeploymentOptions
{
    private const int DefaultSiloCount = 3;
    private const int DefaultSnapshotThreshold = 1024;
    private const int DefaultFirstPublicPort = 5101;
    private const int MinimumPositive = 1;
    private const string SiloCountKey = "KeyLoad:SiloCount";
    private const string ReplicaCountKey = "KeyLoad:ReplicaCount";
    private const string SnapshotKey = "KeyLoad:SnapshotThreshold";
    internal const string FirstPublicPortKey = "KeyLoad:FirstPublicPort";
    private const string LoggingKey = "Logging:LogLevel:Default";
    private const string DefaultLoggingLevel = "Warning";
    internal const string ReplaySection = "KeyLoad:ReplayAdmission";
    internal const int VoterCount = 3;
    internal const string ValidationMessage = "Cluster deployment settings are invalid.";
    /// <summary>Total native Orleans silos, including silos without a physical replica.</summary>
    [ConfigurationKeyName(SiloCountKey)]
    public int SiloCount { get; init; } = DefaultSiloCount;
    /// <summary>Physical voting replicas for the initial database partition.</summary>
    [ConfigurationKeyName(ReplicaCountKey)]
    public int ReplicaCount { get; init; } = DefaultSiloCount;
    [ConfigurationKeyName(SnapshotKey)]
    public int SnapshotThreshold { get; init; } = DefaultSnapshotThreshold;
    [ConfigurationKeyName(FirstPublicPortKey)]
    public int FirstPublicPort { get; init; } = DefaultFirstPublicPort;
    [ConfigurationKeyName(LoggingKey)]
    public string LoggingLevel { get; init; } = DefaultLoggingLevel;
    internal bool IsValid() => SiloCount >= MinimumPositive
        && ReplicaCount >= MinimumPositive && ReplicaCount <= SiloCount
        && SnapshotThreshold >= MinimumPositive && !string.IsNullOrWhiteSpace(LoggingLevel)
        && FirstPublicPort >= MinimumPositive
        && FirstPublicPort <= IPEndPoint.MaxPort - SiloCount + MinimumPositive;
}
