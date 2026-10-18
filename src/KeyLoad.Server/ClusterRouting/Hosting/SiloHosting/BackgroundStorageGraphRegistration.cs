using KeyLoad.Clustering;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Models;

namespace KeyLoad.Server;

/// <summary>Admits only the actual native discovery and durable-job storage reads.</summary>
internal static class BackgroundStorageGraphRegistration
{
    private const string EverySourceMethod = ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod;

    internal static void Add(DirectedGraph graph)
    {
        Due<RecurringDueWorkerGrain>(graph);
        Due<IRecurringDueWorkerGrain>(graph);
        Chunk<SampleChunkWorkerGrain>(graph);
        Chunk<ISampleChunkWorkerGrain>(graph);
        Ann<AnnProjectionWorkerGrain>(graph);
        Ann<IAnnProjectionWorkerGrain>(graph);
        DueCoordinator<RecurringDueCoordinatorGrain>(graph);
        DueCoordinator<IRecurringDueCoordinatorGrain>(graph);
        ChunkCoordinator<SampleChunkCoordinatorGrain>(graph);
        ChunkCoordinator<ISampleChunkCoordinatorGrain>(graph);
        AnnCoordinator<AnnProjectionCoordinatorGrain>(graph);
        AnnCoordinator<IAnnProjectionCoordinatorGrain>(graph);
        Storage<PartitionStorageGrainService>(graph,
            nameof(IBackgroundStorageOperations.ReadDueWorkPageAsync),
            nameof(IBackgroundStorageOperations.ReadQueueDeadlinePageAsync),
            nameof(IBackgroundStorageOperations.ReadQueueTransferPendingAsync),
            nameof(IBackgroundStorageOperations.ReadSampleChunkWorkPageAsync),
            nameof(IBackgroundStorageOperations.ReadAnnProjectionWorkPageAsync),
            nameof(IBackgroundStorageOperations.ReadDueCreatorAsync),
            nameof(IBackgroundStorageOperations.ReadQueueDeadlineCreatorAsync),
            nameof(IBackgroundStorageOperations.ReadTransferCoordinatorAsync),
            nameof(IBackgroundStorageOperations.ReadSampleChunkCurrentAsync),
            nameof(IBackgroundStorageOperations.ReadSampleChunkCreatorAsync),
            nameof(IBackgroundStorageOperations.PrepareAnnProjectionAsync),
            nameof(IBackgroundStorageOperations.ReadAnnProjectionCreatorAsync),
            nameof(IBackgroundStorageOperations.ValidateAnnProjectionResultAsync));
    }

    private static void Due<T>(DirectedGraph graph) => Storage<T>(graph,
        nameof(IBackgroundStorageOperations.ReadDueWorkPageAsync),
        nameof(IBackgroundStorageOperations.ReadQueueDeadlinePageAsync),
        nameof(IBackgroundStorageOperations.ReadQueueTransferPendingAsync));

    private static void Chunk<T>(DirectedGraph graph) => Storage<T>(graph,
        nameof(IBackgroundStorageOperations.ReadSampleChunkWorkPageAsync));

    private static void Ann<T>(DirectedGraph graph)
    {
        Storage<T>(graph, nameof(IBackgroundStorageOperations.ReadAnnProjectionWorkPageAsync));
        Add<T, IDatabaseCommitGrain>(graph, nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
    }

    private static void DueCoordinator<T>(DirectedGraph graph) => Storage<T>(graph,
        nameof(IBackgroundStorageOperations.ReadDueCreatorAsync),
        nameof(IBackgroundStorageOperations.ReadQueueDeadlineCreatorAsync),
        nameof(IBackgroundStorageOperations.ReadTransferCoordinatorAsync));

    private static void ChunkCoordinator<T>(DirectedGraph graph) => Storage<T>(graph,
        nameof(IBackgroundStorageOperations.ReadSampleChunkCurrentAsync),
        nameof(IBackgroundStorageOperations.ReadSampleChunkCreatorAsync));

    private static void AnnCoordinator<T>(DirectedGraph graph)
    {
        Storage<T>(graph, nameof(IBackgroundStorageOperations.PrepareAnnProjectionAsync),
            nameof(IBackgroundStorageOperations.ReadAnnProjectionCreatorAsync),
            nameof(IBackgroundStorageOperations.ValidateAnnProjectionResultAsync));
        Add<T, ISiloDatabaseService>(graph, nameof(ISiloDatabaseService.ReadStatusAsync));
        Add<T, IDatabaseCommitGrain>(graph, nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
    }

    private static void Storage<T>(DirectedGraph graph, params string[] methods)
    {
        foreach (var method in methods)
        {
            Add<T, IBackgroundStorageOperations>(graph, method);
        }
    }

    private static void Add<TSource, TTarget>(DirectedGraph graph, string targetMethod)
        => graph.AddTransition(typeof(TSource).FullName!, typeof(TTarget).FullName!,
            new GrainTransition(EverySourceMethod, targetMethod));
}
