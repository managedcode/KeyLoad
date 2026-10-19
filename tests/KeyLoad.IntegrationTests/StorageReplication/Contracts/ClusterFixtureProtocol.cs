namespace KeyLoad.IntegrationTests.Features.StorageReplication;

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
    internal const string ProfileFileName = "local-profile.json";
    internal const int MaximumProfileBytes = 8192;
    internal const string AdminKeyProperty = "AdminKey";
    internal const string DataRootArgument = "--KeyLoad:DataRoot=";
    internal const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    internal const string SiloCountArgument = "--KeyLoad:SiloCount=3";
    internal const string ReplicaCountArgument = "--KeyLoad:ReplicaCount=";
    internal const string StorageModeArgument = "--KeyLoad:StorageMode=";
    internal const string CommandBytesSetting = "KeyLoad__CommandAdmission__MaxRetainedBytes";
    internal const string DatabaseLimitsSettingPrefix = "KeyLoad__DatabaseLimits__";
    internal const string HttpBodyBytesSetting = "KeyLoad__HttpAdmission__MaxBodyBytes";
    internal const string HttpControlBodyBytesSetting = "KeyLoad__HttpAdmission__MaxControlBodyBytes";
    internal const string HttpReservedBytesSetting = "KeyLoad__HttpAdmission__MaxReservedBytes";
    internal const string HttpHeavyReadBytesSetting = "KeyLoad__HttpAdmission__HeavyReadReservedBytes";
    internal const string HttpRequestsSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.MaxRequests);
    internal const string HttpTenantRequestsSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.MaxTenantRequests);
    internal const string HttpPrincipalRequestsSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.MaxPrincipalRequests);
    internal const string HttpControlRequestsSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.ReservedControlRequests);
    internal const string HttpControlReservedBytesSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.ReservedControlBytes);
    internal const string HttpTenantControlRequestsSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.MaxTenantControlRequests);
    internal const string HttpPrincipalControlRequestsSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.MaxPrincipalControlRequests);
    internal const string HttpOtherReservedBytesSetting = "KeyLoad__HttpAdmission__" + nameof(HttpAdmissionLimits.OtherReservedBytes);
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
