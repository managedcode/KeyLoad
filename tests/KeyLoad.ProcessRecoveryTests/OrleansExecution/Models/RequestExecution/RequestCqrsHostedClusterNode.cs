namespace KeyLoad.ProcessRecoveryTests.OrleansExecution;

/// <summary>Authenticated in-memory membership settings for one production host in an owned two-silo test cluster.</summary>
internal sealed class RequestCqrsHostedClusterNode(string clusterId, string siloName, Guid incarnation,
    string signingKey, string membershipSecret, int siloPort, int siloCount, int replicaCount)
{
    internal string ClusterId { get; } = clusterId;
    internal string SiloName { get; } = siloName;
    internal Guid Incarnation { get; } = incarnation;
    internal string SigningKey { get; } = signingKey;
    internal int SiloPort { get; } = siloPort;
    internal int SiloCount { get; } = siloCount;
    internal int ReplicaCount { get; } = replicaCount;
    internal string MembershipSecret { get; } = membershipSecret;
}
