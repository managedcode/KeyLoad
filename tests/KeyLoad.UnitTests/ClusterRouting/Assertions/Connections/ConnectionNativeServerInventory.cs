using System.Collections.Concurrent;
using KeyLoad.Clustering;
using KeyLoad.RecoveryHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Accounts every actual connection activation using only captured DI service identities and registered consumer IDs.</summary>
internal sealed class ConnectionNativeServerInventory
{
    private const int NoActivations = 0;
    private const long ManagementKey = 0;
    private const string InvalidInventory = "The native connection inventory has an unknown owner or a wrong service silo.";
    private readonly Dictionary<GrainId, SiloAddress> services = [];
    private readonly ConcurrentDictionary<GrainId, byte> consumers = new();
    private readonly IGrainFactory grains;

    internal ConnectionNativeServerInventory(ControlledPartitionMovementNativeHosts hosts,
        IGrainFactory grains, Guid consumer)
    {
        this.grains = grains;
        foreach (var node in hosts.Nodes)
        {
            var id = ((GrainReference)grains.GetGrain<IConnectionGrain>(node.Silo.InternalConnectionId)).GrainId;
            var actual = SiloAddress.FromParsableString(node.CallerSiloAddress);
            if (!services.TryAdd(id, actual))
            {
                throw new InvalidOperationException(InvalidInventory);
            }
        }
        if (services.Count != ControlledPartitionMovementNativeHostEndpoints.OwnerCount)
        {
            throw new InvalidOperationException(InvalidInventory);
        }
        RegisterConsumer(consumer);
    }

    internal void RegisterConsumer(Guid id)
    {
        var grain = ((GrainReference)grains.GetGrain<IConnectionGrain>(id)).GrainId;
        if (id == Guid.Empty || services.ContainsKey(grain))
        {
            throw new InvalidOperationException(InvalidInventory);
        }
        consumers.TryAdd(grain, default);
    }

    internal async Task<HashSet<GrainId>> ReadAsync(CancellationToken token)
    {
        var management = grains.GetGrain<IManagementGrain>(ManagementKey);
        var all = await management.GetDetailedGrainStatistics(null, null, token);
        var active = new HashSet<GrainId>();
        var seen = new HashSet<GrainId>();
        var serviceCount = NoActivations;
        var total = NoActivations;
        foreach (var row in all.Where(row => row.GrainId.Type == GrainType.Create(GrainRoutingProtocol.RequestAlias)))
        {
            total++;
            if (!seen.Add(row.GrainId))
            {
                throw new InvalidOperationException(InvalidInventory);
            }
            if (services.TryGetValue(row.GrainId, out var expected))
            {
                if (!expected.Equals(row.SiloAddress))
                {
                    throw new InvalidOperationException(InvalidInventory);
                }
                serviceCount++;
            }
            else if (!consumers.ContainsKey(row.GrainId) || !active.Add(row.GrainId))
            {
                throw new InvalidOperationException(InvalidInventory);
            }
        }
        if (total != serviceCount + active.Count)
        {
            throw new InvalidOperationException(InvalidInventory);
        }
        Console.WriteLine(FormattableString.Invariant(
            $"ConnectionNativeInventory total={total} services={serviceCount} consumers={active.Count}"));
        return active;
    }
}
