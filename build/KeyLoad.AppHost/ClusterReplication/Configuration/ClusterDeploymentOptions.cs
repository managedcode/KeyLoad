using System.Net;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Independent Orleans membership and physical replica deployment settings.</summary>
[ConfigurationOptions]
internal sealed record ClusterDeploymentOptions
{
    private const int DefaultSiloCount = 3;
    private const int DefaultFirstPublicPort = 5101;
    private const int MinimumPositive = 1;
    private const string SiloCountKey = "KeyLoad:SiloCount";
    private const string ReplicaCountKey = "KeyLoad:ReplicaCount";
    internal const string FirstPublicPortKey = "KeyLoad:FirstPublicPort";
    private const string LoggingKey = "Logging:LogLevel:Default";
    private const string DefaultLoggingLevel = "Warning";
    internal const string ValidationMessage = "Cluster deployment settings are invalid.";
    /// <summary>Total native Orleans silos, each owning local ZoneTree storage.</summary>
    [ConfigurationKeyName(SiloCountKey)]
    public int SiloCount { get; init; } = DefaultSiloCount;
    /// <summary>Persisted data copies, configured independently of the silo count.</summary>
    [ConfigurationKeyName(ReplicaCountKey)]
    public int ReplicaCount { get; init; } = DefaultSiloCount;
    [ConfigurationKeyName(FirstPublicPortKey)]
    public int FirstPublicPort { get; init; } = DefaultFirstPublicPort;
    [ConfigurationKeyName(LoggingKey)]
    public string LoggingLevel { get; init; } = DefaultLoggingLevel;
    internal bool IsValid() => SiloCount >= MinimumPositive
        && ReplicaCount >= MinimumPositive && ReplicaCount <= SiloCount
        && !string.IsNullOrWhiteSpace(LoggingLevel)
        && FirstPublicPort >= MinimumPositive
        && FirstPublicPort <= IPEndPoint.MaxPort;
}
