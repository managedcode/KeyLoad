using System.Net;
using KeyLoad.Clustering;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Orleans deployment size, physical storage access and required data copies.</summary>
[ConfigurationOptions]
internal sealed record ClusterDeploymentOptions
{
    private const int DefaultSiloCount = 3;
    private const int DefaultFirstPublicPort = 5101;
    private const string SiloCountKey = "KeyLoad:SiloCount";
    private const string ReplicaCountKey = "KeyLoad:ReplicaCount";
    private const string StorageModeKey = "KeyLoad:StorageMode";
    internal const string FirstPublicPortKey = "KeyLoad:FirstPublicPort";
    private const string LoggingKey = "Logging:LogLevel:Default";
    private const string DefaultLoggingLevel = "Warning";
    internal const string ValidationMessage = "Cluster deployment settings are invalid.";
    /// <summary>Total execution Orleans silos.</summary>
    [ConfigurationKeyName(SiloCountKey)]
    public int SiloCount { get; init; } = DefaultSiloCount;
    /// <summary>Persisted data copies, configured independently of the silo count.</summary>
    [ConfigurationKeyName(ReplicaCountKey)]
    public int ReplicaCount { get; init; } = DefaultSiloCount;
    /// <summary>Whether nodes receive isolated directories or the same shared directory.</summary>
    [ConfigurationKeyName(StorageModeKey)]
    public StorageAccessMode StorageMode { get; init; } = StorageAccessMode.IsolatedVolumes;
    [ConfigurationKeyName(FirstPublicPortKey)]
    public int FirstPublicPort { get; init; } = DefaultFirstPublicPort;
    [ConfigurationKeyName(LoggingKey)]
    public string LoggingLevel { get; init; } = DefaultLoggingLevel;
    internal bool IsValid() => SiloCount >= 1
        && ReplicaCount >= 1 && ReplicaCount <= SiloCount
        && Enum.IsDefined(StorageMode)
        && (StorageMode != StorageAccessMode.SharedVolume || ReplicaCount == 1)
        && !string.IsNullOrWhiteSpace(LoggingLevel)
        && FirstPublicPort >= 1
        && FirstPublicPort <= IPEndPoint.MaxPort;
}
