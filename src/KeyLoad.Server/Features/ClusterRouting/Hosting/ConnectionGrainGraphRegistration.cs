using KeyLoad.Orleans;
using ManagedCode.Orleans.Graph.Extensions;

namespace KeyLoad.Server;

internal static class ConnectionGrainGraphRegistration
{
    internal static void Register(ISiloBuilder silo, bool privateProbe)
    {
        silo.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AddGrainServiceTransition<SampleChunkGrainService, ISampleChunkCoordinatorGrain>(
                    nameof(ISampleChunkCoordinatorGrain.ScheduleAsync))
                .AddGrainServiceTransition<RecurringDueGrainService, IRecurringDueCoordinatorGrain>(
                    nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync))
                .AddGrainTransition<IRecurringDueCoordinatorGrain, IConnectionGrain>()
                .MethodByName(nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, IConnectionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
            if (privateProbe)
            {
                graph.AllowClientCallGrain<IManagementGrain>()
                    .AddGrainTransition<IConnectionGrain, IManagementGrain>()
                    .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(IManagementGrain.GetActiveGrains)).And();
            }
        });
    }
}
