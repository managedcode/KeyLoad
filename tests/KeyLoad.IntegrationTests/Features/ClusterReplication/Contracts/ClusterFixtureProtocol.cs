namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Stable fixture-only names and profile keys used by the Aspire RF3 test environment.</summary>
internal static class ClusterFixtureProtocol
{
    internal const int FirstNodeNumber = 1;
    internal const int NodeCount = 3;
    internal const int CapturedLogLinesPerNode = 24;
    internal static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(30);
    internal const string NodePrefix = "node";
    internal const string Node1Name = "node1";
    internal const string Node2Name = "node2";
    internal const string Node3Name = "node3";
    internal const string GuidFormat = "N";
    internal const string HttpEndpointName = "http";
    internal const string RootDirectoryPrefix = "keyload-cluster-";
    internal const string SolutionFileName = "KeyLoad.slnx";
    internal const string ProfileFileName = "local-profile.json";
    internal const int MaximumProfileBytes = 8192;
    internal const string AdminKeyProperty = "AdminKey";
    internal const string PeerSecretProperty = "PeerSecret";
    internal const string PhysicalShardIdProperty = "PhysicalShardId";
    internal const string PhysicalShardIdSetting = "KeyLoad__PhysicalShardId";
    internal const string DataRootArgument = "--KeyLoad:DataRoot=";
    internal const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    internal const string SnapshotThresholdArgument = "--KeyLoad:SnapshotThreshold=16";
    internal const string CommandBytesSetting = "KeyLoad__CommandAdmission__MaxRetainedBytes";
    internal const string HttpBodyBytesSetting = "KeyLoad__HttpAdmission__MaxBodyBytes";
    internal const string HttpControlBodyBytesSetting = "KeyLoad__HttpAdmission__MaxControlBodyBytes";
    internal const string HttpReservedBytesSetting = "KeyLoad__HttpAdmission__MaxReservedBytes";
    internal const string HttpHeavyReadBytesSetting = "KeyLoad__HttpAdmission__HeavyReadReservedBytes";
    internal const string HealthCheckLoggerCategory = "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";
    internal const string ArtifactDirectory = "artifacts";
    internal const string QualificationDirectory = "qualification";
    internal const string DiagnosticsFileName = "rf3-failure.log";

    /// <summary>Returns whether the resource name belongs to the fixed three-node cluster.</summary>
    /// <param name="resourceName">The Aspire resource name to validate.</param>
    /// <returns>True only for node1, node2, or node3.</returns>
    internal static bool IsNodeName(string resourceName) => resourceName is Node1Name or Node2Name or Node3Name;

    /// <summary>Creates the unchanged Aspire node resource name for an ordinal.</summary>
    /// <param name="number">A one-based RF3 node number.</param>
    /// <returns>The corresponding node1, node2, or node3 resource name.</returns>
    internal static string NodeName(int number) => number is >= FirstNodeNumber and <= NodeCount
        ? NodePrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(number));
}
