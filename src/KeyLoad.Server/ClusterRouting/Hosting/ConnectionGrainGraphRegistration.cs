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
                .AddGrainServiceTransition<PartitionStorageGrainService, IRequestWorkerGrain>(
                    nameof(IRequestWorkerGrain.ExecuteStreamAsync), nameof(IRequestWorkerGrain.ExecuteCommandAsync));
        });
    }
}
