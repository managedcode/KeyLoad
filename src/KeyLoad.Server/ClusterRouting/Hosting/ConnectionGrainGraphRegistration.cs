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
                .AddGrainServiceTransition<global::KeyLoad.Server.Features.DocumentStorage.RemoteModelGrainService, IConnectionGrain>(
                    nameof(IConnectionGrain.ExecuteStreamAsync))
                .AddGrainServiceTransition<global::KeyLoad.Server.Features.ClusterRouting.PartitionMovementGrainService, IConnectionGrain>(
                    nameof(IConnectionGrain.ExecuteStreamAsync))
                .AddGrainServiceTransition<SampleChunkWorkerGrain, ISampleChunkCoordinatorGrain>(
                    nameof(ISampleChunkCoordinatorGrain.ScheduleAsync))
                .AddGrainServiceTransition<AnnProjectionWorkerGrain, IAnnProjectionCoordinatorGrain>(
                    nameof(IAnnProjectionCoordinatorGrain.ScheduleAsync))
                .AddGrainServiceTransition<RecurringDueWorkerGrain, IRecurringDueCoordinatorGrain>(
                    nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync))
                .AddGrainTransition<AnnProjectionCoordinatorGrain, IConnectionGrain>()
                .MethodByName(nameof(AnnProjectionCoordinatorGrain.ExecuteJobAsync),
                    nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IRecurringDueCoordinatorGrain, IConnectionGrain>()
                .MethodByName(nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, IConnectionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync))
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.PrepareOriginalCommandAsync)).And();
        });
    }
}
