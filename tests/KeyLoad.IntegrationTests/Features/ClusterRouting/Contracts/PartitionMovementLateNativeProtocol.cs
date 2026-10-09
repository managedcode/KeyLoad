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
    internal const string AllowLoopbackHttp = "AllowLoopbackHttp";
    internal const string AllowPrivateNetworkHttp = "AllowPrivateNetworkHttp";
    internal const string PartitionMovementExecutionEnabled = "PartitionMovementExecution:Enabled";
    internal const string MembershipAuthorityMode = "MembershipAuthority:Mode";
    internal const string MembershipAuthorityRegisterPhysicalOwners = "MembershipAuthority:RegisterPhysicalOwners";
    internal const string MembershipAuthorityRemoteDocumentReads = "MembershipAuthority:RemoteDocumentReads";
    internal const string MembershipAuthorityRemotePartitionQueries = "MembershipAuthority:RemotePartitionQueries";
    internal const string TrustedGroupPhysicalShardId = "TrustedGroupPhysicalShardId";
    internal const string TrustedGroupIncarnation = "TrustedGroupIncarnation";
    internal const string TrustedGroupPeerSecret = "TrustedGroupPeerSecret";
    internal const string AuthorityPhysicalShardId = "AuthorityPhysicalShardId";
    internal const string AuthorityIncarnation = "AuthorityIncarnation";
    internal const string AuthorityPeerSecret = "AuthorityPeerSecret";
    internal const string Peers = "Peers:";
    internal const string TrustedGroupVoterIds = "TrustedGroupVoterIds:";
    internal const string AuthorityEndpoints = "AuthorityEndpoints:";
    internal const string TrustedGroupSiloEndpoints = "TrustedGroupSiloEndpoints:";
    internal const string UrlsArgumentPrefix = "--urls=";
    internal const string NodeArgumentPrefix = "--KeyLoad:";
    internal const string MembershipArgumentPrefix = "--KeyLoad:MembershipAuthority:";
    internal const string AtomicPartitionId = "atomic-partition";
}
