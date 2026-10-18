using KeyLoad.Clustering;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Models;
using Orleans.DurableJobs;

namespace KeyLoad.Server;

internal static class NativeJobGraphRegistration
{
    private const string ExistingPolicyRequired = "The native execution graph requires the existing default-deny policy instance.";
    private const string EverySourceMethod = ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod;

    internal static void Extend(IServiceCollection services)
    {
        ExtendPolicy(services, graph =>
        {
            AddRequests(graph);
            AddBackgroundWorkers(graph);
            AddJobs(graph);
            AddJournalStorage(graph);
            AddNativeJournalScheduler(graph, services);
            BackgroundStorageGraphRegistration.Add(graph);
        });
    }

    internal static void ExtendJournalStorage(IServiceCollection services)
    {
        ExtendPolicy(services, graph =>
        {
            AddRequests(graph);
            AddJournalStorage(graph);
        });
    }

    private static void ExtendPolicy(IServiceCollection services, Action<DirectedGraph> configure)
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
        configure(graph);
        services.Remove(descriptor);
        services.AddSingleton(new GrainTransitionManager(graph, allowAllByDefault: false));
    }

    private static void AddRequests(DirectedGraph graph)
    {
        Add<IConnectionGrain, IDatabaseCommitGrain>(graph, nameof(IConnectionGrain.ExecuteStreamAsync),
            nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
        Add<ICommandPartitionGrain, IDatabaseCommitGrain>(graph, nameof(ICommandPartitionGrain.ExecuteAsync),
            nameof(IDatabaseCommitGrain.GetReadOwnerAsync));
        Add<IConnectionGrain, ICommandPartitionGrain>(graph, nameof(IConnectionGrain.ExecuteStreamAsync),
            nameof(ICommandPartitionGrain.ExecuteAsync));
        Add<IConnectionGrain, IRequestWorkerGrain>(graph, nameof(IConnectionGrain.ExecuteStreamAsync),
            nameof(IRequestWorkerGrain.ExecuteAsync));
        Add<ICommandPartitionGrain, IRequestWorkerGrain>(graph, nameof(ICommandPartitionGrain.ExecuteAsync),
            nameof(IRequestWorkerGrain.ExecuteCommandAsync));
        AddSharedQueries(graph);
    }

    private static void AddSharedQueries(DirectedGraph graph)
    {
        foreach (var target in new[] { nameof(IPartitionQueryGrain.ResolveOwnerAsync), nameof(IPartitionQueryGrain.ExecuteAsync) })
        {
            graph.AddTransition(typeof(IDatabaseCommitGrain).FullName!, typeof(IPartitionQueryGrain).FullName!,
                new GrainTransition(nameof(IDatabaseCommitGrain.ExecuteSharedAsync), target, IsReentrant: true));
        }
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
    }

    private static void AddJobs(DirectedGraph graph)
    {
        Add<RecurringDueCoordinatorGrain, IConnectionGrain>(graph, EverySourceMethod, nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<SampleChunkCoordinatorGrain, IConnectionGrain>(graph, nameof(IDurableJobHandler.ExecuteJobAsync),
            nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<AnnProjectionCoordinatorGrain, IConnectionGrain>(graph, nameof(IDurableJobHandler.ExecuteJobAsync),
            nameof(IConnectionGrain.ExecuteStreamAsync));
        Add<IRecurringDueCoordinatorGrain, IConnectionGrain>(graph, EverySourceMethod, nameof(IConnectionGrain.ExecuteStreamAsync));
    }

    private static void AddJournalStorage(DirectedGraph graph)
    {
        graph.AddTransition(RuntimeJournalClient.CallerIdentity, typeof(IConnectionGrain).FullName!,
            new GrainTransition(RuntimeJournalClient.ReadCoreCallerMethod, nameof(IConnectionGrain.ExecuteStreamAsync)));
        graph.AddTransition(RuntimeJournalClient.CallerIdentity, typeof(IConnectionGrain).FullName!,
            new GrainTransition(RuntimeJournalClient.SendCommandCallerMethod, nameof(IConnectionGrain.ExecuteStreamAsync)));
    }

    private static void AddNativeJournalScheduler(DirectedGraph graph, IServiceCollection services)
    {
        var contract = typeof(ILocalDurableJobManager);
        var source = services.Where(static service => !service.IsKeyedService)
            .Select(static service => service.ImplementationType).OfType<Type>()
            .Single(type => type.Assembly == contract.Assembly && contract.IsAssignableFrom(type));
        graph.AddTransition(source.FullName!, typeof(IConnectionGrain).FullName!,
            new GrainTransition(EverySourceMethod, nameof(IConnectionGrain.ExecuteStreamAsync)));
    }

    private static void Add<TSource, TTarget>(DirectedGraph graph, string sourceMethod, params string[] targetMethods)
    {
        foreach (var method in targetMethods)
        {
            graph.AddTransition(typeof(TSource).FullName!, typeof(TTarget).FullName!, new GrainTransition(sourceMethod, method));
        }
    }
}
