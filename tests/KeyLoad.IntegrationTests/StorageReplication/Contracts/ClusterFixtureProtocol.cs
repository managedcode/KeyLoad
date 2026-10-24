namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Stable fixture-only names and profile keys used by the Aspire Orleans test environment.</summary>
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
    internal const string CoverageDirectoryName = "coverage";
    internal const string HttpEndpointName = "http";
    internal const string RootDirectoryPrefix = "keyload-cluster-";
    internal const string SolutionFileName = "KeyLoad.slnx";
    internal const string DataRootArgument = "--KeyLoad:DataRoot=";
    internal const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    internal const string SiloCountArgument = "--KeyLoad:SiloCount=3";
    internal const string ReplicaCountArgument = "--KeyLoad:ReplicaCount=";
    internal const string StorageModeArgument = "--KeyLoad:StorageMode=";
    internal const string DatabaseLimitsSettingPrefix = "KeyLoad__DatabaseLimits__";
    internal const string BodyBytesSetting = "KeyLoad__ServerExecution__MaximumBodyBytes";
    internal const string HealthCheckLoggerCategory = "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";
    internal const string ArtifactDirectory = "artifacts";
    internal const string QualificationDirectory = "qualification";
    internal const string DiagnosticsFileName = "cluster-failure.log";

    /// <summary>Returns whether the resource name belongs to the fixed three-node cluster.</summary>
    /// <param name="resourceName">The Aspire resource name to validate.</param>
    /// <returns>True only for node1, node2, or node3.</returns>
    internal static bool IsNodeName(string resourceName) => resourceName is Node1Name or Node2Name or Node3Name;

    /// <summary>Creates the unchanged Aspire node resource name for an ordinal.</summary>
    /// <param name="number">A one-based Orleans node number.</param>
    /// <returns>The corresponding node1, node2, or node3 resource name.</returns>
    internal static string NodeName(int number) => number is >= FirstNodeNumber and <= NodeCount
        ? NodePrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(number));
}
