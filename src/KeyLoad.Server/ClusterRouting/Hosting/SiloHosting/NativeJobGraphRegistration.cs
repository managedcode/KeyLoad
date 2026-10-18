using KeyLoad.Clustering;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Models;
using Orleans.DurableJobs;

namespace KeyLoad.Server;

internal static class NativeJobGraphRegistration
{
    private const string ExistingPolicyRequired = "The native jobs graph requires the existing default-deny policy instance.";
    private const string EverySourceMethod = ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod;

    internal static void Extend(IServiceCollection services)
    {
        var descriptor = services.Single(static value => !value.IsKeyedService
            && value.ServiceType == typeof(GrainTransitionManager));
        if (descriptor.ImplementationInstance is not GrainTransitionManager existing)
        {
            throw new InvalidOperationException(ExistingPolicyRequired);
        }
        var graph = new DirectedGraph(allowSelfLoops: true);
        foreach (var edge in existing.GetPolicyEdges())
        {
            foreach (var transition in edge.Transitions)
            {
                graph.AddTransition(edge.Source, edge.Target, transition);
            }
        }
        AddRequests(graph);
        AddBackgroundWorkers(graph);
        AddJobs(graph);
        services.Remove(descriptor);
        services.AddSingleton(new GrainTransitionManager(graph, allowAllByDefault: false));
    }

    private static void AddRequests(DirectedGraph graph)
    {
        Add<IConnectionGrain, IDatabaseCommitGrain>(graph, nameof(IConnectionGrain.ExecuteStreamAsync),
            nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
        Add<IConnectionGrain, IRequestWorkerGrain>(graph, nameof(IConnectionGrain.ExecuteStreamAsync),
            nameof(IRequestWorkerGrain.ExecuteStreamAsync));
        Add<ICommandPartitionGrain, IDatabaseCommitGrain>(graph, nameof(ICommandPartitionGrain.ExecuteAsync),
            nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
        Add<ICommandPartitionGrain, IRequestWorkerGrain>(graph, nameof(ICommandPartitionGrain.ExecuteAsync),
            nameof(IRequestWorkerGrain.ExecuteCommandAsync));
        Add<IRequestWorkerGrain, ICommandPartitionGrain>(graph, nameof(IRequestWorkerGrain.ExecuteStreamAsync),
            nameof(ICommandPartitionGrain.ExecuteAsync));
        Add<IRequestWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod,
            nameof(IDatabaseCommitGrain.ReadCutAsync), nameof(IDatabaseCommitGrain.ApplyAsync));
        Add<IRequestWorkerGrain, IConnectionGrain>(graph, EverySourceMethod,
            nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<IConnectionGrain, IConnectionGrain>(graph, nameof(IConnectionGrain.ExecuteStreamAsync),
            nameof(IConnectionGrain.ExecuteStreamAsync));
    }

    private static void AddBackgroundWorkers(DirectedGraph graph)
    {
        Add<RecurringDueWorkerGrain, IRecurringDueCoordinatorGrain>(graph, EverySourceMethod,
            nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IRecurringDueCoordinatorGrain.ProcessQueueDeadlineAsync),
            nameof(IRecurringDueCoordinatorGrain.ProcessQueueTransferAsync));
        Add<IRecurringDueWorkerGrain, IRecurringDueCoordinatorGrain>(graph, EverySourceMethod,
            nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IRecurringDueCoordinatorGrain.ProcessQueueDeadlineAsync),
            nameof(IRecurringDueCoordinatorGrain.ProcessQueueTransferAsync));
        Add<SampleChunkWorkerGrain, ISampleChunkCoordinatorGrain>(graph, EverySourceMethod,
            nameof(ISampleChunkCoordinatorGrain.ScheduleAsync));
        Add<ISampleChunkWorkerGrain, ISampleChunkCoordinatorGrain>(graph, EverySourceMethod,
            nameof(ISampleChunkCoordinatorGrain.ScheduleAsync));
        Add<AnnProjectionWorkerGrain, IAnnProjectionCoordinatorGrain>(graph, EverySourceMethod,
            nameof(IAnnProjectionCoordinatorGrain.ScheduleAsync));
        Add<IAnnProjectionWorkerGrain, IAnnProjectionCoordinatorGrain>(graph, EverySourceMethod,
            nameof(IAnnProjectionCoordinatorGrain.ScheduleAsync));
        Add<RecurringDueWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<SampleChunkWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<AnnProjectionWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<IRecurringDueWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<ISampleChunkWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<IAnnProjectionWorkerGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
    }

    private static void AddJobs(DirectedGraph graph)
    {
        Add<RecurringDueCoordinatorGrain, IConnectionGrain>(graph, EverySourceMethod, nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<SampleChunkCoordinatorGrain, IConnectionGrain>(graph, nameof(IDurableJobHandler.ExecuteJobAsync),
            nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<AnnProjectionCoordinatorGrain, IConnectionGrain>(graph, nameof(IDurableJobHandler.ExecuteJobAsync),
            nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<IRecurringDueCoordinatorGrain, IConnectionGrain>(graph, EverySourceMethod, nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<IRecurringDueCoordinatorGrain, IDatabaseCommitGrain>(graph, EverySourceMethod,
            nameof(IDatabaseCommitGrain.ReadCutAsync), nameof(IDatabaseCommitGrain.ApplyAsync));
        Add<RecurringDueCoordinatorGrain, IDatabaseCommitGrain>(graph, EverySourceMethod,
            nameof(IDatabaseCommitGrain.ReadCutAsync), nameof(IDatabaseCommitGrain.ApplyAsync));
        Add<SampleChunkCoordinatorGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<AnnProjectionCoordinatorGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<ISampleChunkCoordinatorGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        Add<IAnnProjectionCoordinatorGrain, IDatabaseCommitGrain>(graph, EverySourceMethod, nameof(IDatabaseCommitGrain.ReadCutAsync));
        graph.AddTransition(RuntimeJournalClient.CallerIdentity, typeof(IConnectionGrain).FullName!,
            new GrainTransition(RuntimeJournalClient.ReadCoreCallerMethod, nameof(IConnectionGrain.ExecuteStreamAsync)));
        graph.AddTransition(RuntimeJournalClient.CallerIdentity, typeof(IConnectionGrain).FullName!,
            new GrainTransition(RuntimeJournalClient.SendCommandCallerMethod, nameof(IConnectionGrain.ExecuteStreamAsync)));
    }

    private static void Add<TSource, TTarget>(DirectedGraph graph, string sourceMethod, params string[] targetMethods)
    {
        foreach (var method in targetMethods)
        {
            graph.AddTransition(typeof(TSource).FullName!, typeof(TTarget).FullName!, new GrainTransition(sourceMethod, method));
        }
    }
}
