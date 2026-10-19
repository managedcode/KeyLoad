using System.Net;

namespace KeyLoad.RecoveryTests.Features.OrleansExecution;

/// <summary>Execution localhost settings for one production host in an owned two-silo test cluster.</summary>
internal sealed class RequestCqrsHostedClusterNode(string clusterId, string siloName, Guid incarnation,
    string signingKey, int siloPort, int primaryPort, int siloCount, int replicaCount)
{
    internal string ClusterId { get; } = clusterId;
    internal string SiloName { get; } = siloName;
    internal Guid Incarnation { get; } = incarnation;
    internal string SigningKey { get; } = signingKey;
    internal int SiloPort { get; } = siloPort;
    internal int SiloCount { get; } = siloCount;
    internal int ReplicaCount { get; } = replicaCount;
    internal IPEndPoint PrimaryEndpoint => new(IPAddress.Loopback, primaryPort);
}
