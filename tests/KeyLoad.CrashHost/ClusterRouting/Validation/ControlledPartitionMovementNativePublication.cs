using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Checks actual local native publication from the same production application.</summary>
internal static class ControlledPartitionMovementNativePublication
{
    internal static void RequireActive(WebApplication application, PartitionHost partition, OrleansNode node)
    {
        var services = application.Services;
        var local = services.GetRequiredService<ILocalSiloDetails>();
        var status = services.GetRequiredService<ISiloStatusOracle>();
        var discovery = node.Discovery?.Read();
        if (status.CurrentStatus != SiloStatus.Active || discovery is not { TransportReady: true }
            || !string.Equals(local.SiloAddress.ToParsableString(), discovery.SiloAddress, StringComparison.Ordinal)
            || !ReferenceEquals(services.GetRequiredService<IGrainFactory>(), node.Grains)
            || !ReferenceEquals(services.GetRequiredService<DatabaseEngine>(), partition.Database)
            || !ReferenceEquals(services.GetRequiredService<Func<IReplicaEndpoint>>()(), partition.Consensus))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        }
    }

    internal static void RequireClosed(OrleansNode? node, ReplicaSiloDiscoveryState? discovery)
    {
        if (node?.HasJoinedRequestWork == false || discovery?.Read().TransportReady == true)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        }
    }
}
