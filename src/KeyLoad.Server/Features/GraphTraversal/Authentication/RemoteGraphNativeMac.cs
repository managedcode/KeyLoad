using System.Security.Cryptography;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeMac
{
    internal static PartitionMovementMac Create(NodeOptions options, PartitionHost partition, RegisteredPhysicalOwnerV1 owner)
    {
        RemoteGraphNativeIdentity.RequireOwner(options, partition, owner);
        var configured = PartitionMovementNativeDiscoveryIdentity.Secret(options, partition.Configuration, owner);
        var key = Convert.FromBase64String(configured);
        try
        { return new(key, partition.Database.Limits.MaxBatchBytes); }
        finally
        { CryptographicOperations.ZeroMemory(key); }
    }
}
