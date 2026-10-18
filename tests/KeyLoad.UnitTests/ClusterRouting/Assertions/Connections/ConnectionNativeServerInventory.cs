using System.Collections.Concurrent;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Accounts every actual connection activation using only captured DI service identities and registered consumer IDs.</summary>
internal sealed class ConnectionNativeServerInventory
{
    private const int NoActivations = 0;
    private const long ManagementKey = 0;
    private const string InvalidInventory = "The native connection inventory has an unknown owner or a wrong service silo.";
    private readonly HashSet<GrainId> services = [];
    private readonly HashSet<SiloAddress> silos = [];
    private readonly ConcurrentDictionary<GrainId, byte> consumers = new();
    private readonly IGrainFactory grains;
    private readonly GrainType connectionGrainType;

    internal ConnectionNativeServerInventory(RequestCqrsHostedCluster hosts,
        IGrainFactory grains, Guid consumer)
    {
        this.grains = grains;
        connectionGrainType = ((GrainReference)grains.GetGrain<IConnectionGrain>(consumer)).GrainId.Type;
        foreach (var node in hosts.Nodes)
        {
            var id = ((GrainReference)grains.GetGrain<IConnectionGrain>(node.ConnectionId)).GrainId;
            var actual = node.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress;
            if (id.Type != connectionGrainType || !services.Add(id) || !silos.Add(actual))
            {
                throw new InvalidOperationException(InvalidInventory);
            }
        }
        if (services.Count != ConnectionNativeServerEvidence.ExpectedNativeFiles)
        {
            throw new InvalidOperationException(InvalidInventory);
        }
        RegisterConsumer(consumer);
    }

    internal void RegisterConsumer(Guid id)
    {
        var grain = ((GrainReference)grains.GetGrain<IConnectionGrain>(id)).GrainId;
        if (id == Guid.Empty || grain.Type != connectionGrainType || services.Contains(grain))
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
        foreach (var row in all.Where(row => row.GrainId.Type == connectionGrainType))
        {
            total++;
            if (!seen.Add(row.GrainId) || !silos.Contains(row.SiloAddress))
            {
                throw new InvalidOperationException(InvalidInventory);
            }
            if (services.Contains(row.GrainId))
            {
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
