using KeyLoad.Clustering;
using ManagedCode.Orleans.Graph.Extensions;

namespace KeyLoad.Server;

internal static class ConnectionGrainGraphRegistration
{
    internal static void Register(ISiloBuilder silo)
    {
        silo.AddOrleansGraph(configureFilters: static filters => filters.EnableLiveGraphTelemetry = false, configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AllowClientCallGrain<IUserGrain>()
                .AllowClientCallGrain<IDatabaseCommitGrain>()
                .AllowClientCallGrain<IRecurringDueWorkerGrain>()
                .AllowClientCallGrain<ISampleChunkWorkerGrain>()
                .AllowClientCallGrain<IAnnProjectionWorkerGrain>()
                .AddGrainServiceTransition<PartitionStorageGrainService, IDatabaseCommitGrain>(
                    nameof(IDatabaseCommitGrain.ReadCutAsync), nameof(IDatabaseCommitGrain.ApplyAsync),
                    nameof(IDatabaseCommitGrain.AbortSharedMaintenanceAsync),
                    nameof(IDatabaseCommitGrain.ReadSharedStatusAsync),
                    nameof(IDatabaseCommitGrain.ReadSharedDashboardAsync), nameof(IDatabaseCommitGrain.CreateSharedBackupAsync));
            graph.AddGrainTransition<IPartitionQueryGrain, IDatabaseCommitGrain>()
                .MethodByName(nameof(IPartitionQueryGrain.ResolveOwnerAsync), nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
        });
    }
}
