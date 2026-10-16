using System.Security.Cryptography;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceOwnerInventoryNativeTrust(PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits)
{
    internal PartitionMovementMac CreateMac()
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options.Value, partition.Configuration);
        var configured = PartitionMovementNativeDiscoveryIdentity.Secret(options.Value, partition.Configuration, control);
        var key = Convert.FromBase64String(configured);
        try
        {
            return new(key, limits.Value.MaxBatchBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
