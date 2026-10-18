using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Captures the actual source configuration before joined shutdown for strictly offline native cold reads.</summary>
internal sealed class ConnectionNativeColdSource
{
    private readonly string directory;
    private readonly ServerRuntimeOptions options;
    private readonly IAuthorizationPolicy authorization;
    private readonly PhysicalShardRecord physicalOwner;
    private readonly DurabilityProfile durability;
    internal StoreIdentity Identity
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    internal DatabaseLimits Limits => options.Core.DatabaseLimits.Value;
    internal IOptions<GrainRoutingOptions> RoutingOptions => options.GrainRouting;

    internal ConnectionNativeColdSource(ControlledPartitionMovementNativeHost source)
    {
        directory = source.Partition.DirectoryPath;
        options = source.Runtime;
        authorization = source.Database.Authorization;
        physicalOwner = source.Application.Services.GetRequiredService<IPhysicalRequestPlacement>().Owner;
        durability = source.Database.Durability;
        Identity = source.Store.Identity;
        Clock = source.Clock;
    }

    internal PartitionStores OpenStores()
        => new(options.Node, directory, options.StorageExecution, options.PointCache, Clock);

    internal DatabaseEngine CreateDatabase(ZoneTreeStore store)
    {
        var core = options.Core;
        var database = new DatabaseEngine(store, authorization, core.DatabaseLimits,
            core.DueWork, core.EventSource, core.Messaging, core.GraphExecution, core.ChangeFeedExecution,
            core.BlobExecution, core.NativeClaimsExecution, core.TimeSeriesExecution,
            Clock, physicalOwner)
        {
            Durability = durability
        };
        database.ConfigureRuntimeJournal(core.RuntimeJournal);
        return database;
    }
}
