namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Exact configuration and partition keys for the genuine six-owner late-Retire fixture.</summary>
internal static class PartitionMovementLateNativeProtocol
{
    internal const string DataDirectory = "DataDirectory";
    internal const string PublicEndpoint = "PublicEndpoint";
    internal const string ClusterId = "ClusterId";
    internal const string PhysicalShardId = "PhysicalShardId";
    internal const string Incarnation = "Incarnation";
    internal const string SigningKey = "SigningKey";
    internal const string PeerSecret = "PeerSecret";
    internal const string AdminKey = "AdminKey";
    internal const string SiloAddress = "SiloAddress";
    internal const string SiloPort = "SiloPort";
    internal const string AllowLoopbackHttp = "AllowLoopbackHttp";
    internal const string AllowPrivateNetworkHttp = "AllowPrivateNetworkHttp";
    internal const string PartitionMovementExecutionEnabled = "PartitionMovementExecution:Enabled";
    internal const string PhysicalGroupTrustRole = "PhysicalGroupTrust:Role";
    internal const string RegisterPhysicalOwners = "PhysicalGroupTrust:RegisterPhysicalOwners";
    internal const string RemoteDocumentReads = "PhysicalGroupTrust:RemoteDocumentReads";
    internal const string RemotePartitionQueries = "PhysicalGroupTrust:RemotePartitionQueries";
    internal const string ForeignPhysicalShardId = "ForeignPhysicalShardId";
    internal const string ForeignIncarnation = "ForeignIncarnation";
    internal const string ForeignPeerSecret = "ForeignPeerSecret";
    internal const string Peers = "Peers:";
    internal const string ForeignVoterIds = "ForeignVoterIds:";
    internal const string UrlsArgumentPrefix = "--urls=";
    internal const string NodeArgumentPrefix = "--KeyLoad:";
    internal const string TrustArgumentPrefix = "--KeyLoad:PhysicalGroupTrust:";
    internal const string AtomicPartitionId = "atomic-partition";
}
