using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Captures the actual execution activation and its physical storage session without retaining a context.</summary>
internal sealed record QueryHostedStorageOwner(GrainId GrainId, ActivationId ActivationId, SiloAddress Silo,
    ZoneTreeStore Store, Guid NodeId, TimeProvider Clock)
{
    internal static QueryHostedStorageOwner Capture(IGrainContext context)
    {
        var runtime = context.ActivationServices;
        var physical = runtime.GetRequiredService<PartitionHostApplicationOwner>().Session.Services;
        var store = (ZoneTreeStore)physical.GetRequiredService<DatabaseEngine>().Store;
        return new(context.GrainId, context.ActivationId,
            runtime.GetRequiredService<ILocalSiloDetails>().SiloAddress,
            store, store.Identity.NodeId, physical.GetRequiredService<TimeProvider>());
    }
}
