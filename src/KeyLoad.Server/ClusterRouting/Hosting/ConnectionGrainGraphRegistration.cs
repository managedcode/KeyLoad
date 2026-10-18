using KeyLoad.Clustering;
using ManagedCode.Orleans.Graph.Extensions;

namespace KeyLoad.Server;

internal static class ConnectionGrainGraphRegistration
{
    internal static void Register(ISiloBuilder silo)
    {
        silo.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AllowClientCallGrain<IDatabaseCommitGrain>()
                .AllowClientCallGrain<IRecurringDueWorkerGrain>()
                .AllowClientCallGrain<ISampleChunkWorkerGrain>()
                .AllowClientCallGrain<IAnnProjectionWorkerGrain>()
                .AddGrainServiceTransition<PartitionStorageGrainService, IDatabaseCommitGrain>(
                    nameof(IDatabaseCommitGrain.ReadCutAsync), nameof(IDatabaseCommitGrain.ApplyAsync),
                    nameof(IDatabaseCommitGrain.ExecuteSharedAsync), nameof(IDatabaseCommitGrain.AbortSharedMaintenanceAsync),
                    nameof(IDatabaseCommitGrain.ReadSharedStatusAsync), nameof(IDatabaseCommitGrain.ReadSharedAdmissionAsync),
                    nameof(IDatabaseCommitGrain.ReadSharedDashboardAsync), nameof(IDatabaseCommitGrain.CreateSharedBackupAsync))
                .AddGrainServiceTransition<PartitionStorageGrainService, IPartitionQueryGrain>(
                    nameof(IPartitionQueryGrain.ResolveOwnerAsync), nameof(IPartitionQueryGrain.ExecuteAsync));
            graph.AddGrainTransition<IPartitionQueryGrain, IDatabaseCommitGrain>()
                .MethodByName(nameof(IPartitionQueryGrain.ResolveOwnerAsync), nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
            graph.AddGrainTransition<IDatabaseCommitGrain, IPartitionQueryGrain>()
                .MethodsByName((nameof(IDatabaseCommitGrain.ExecuteSharedAsync), nameof(IPartitionQueryGrain.ResolveOwnerAsync)),
                    (nameof(IDatabaseCommitGrain.ExecuteSharedAsync), nameof(IPartitionQueryGrain.ExecuteAsync)));
        });
    }
}
