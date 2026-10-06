using KeyLoad.Orleans;
using ManagedCode.Orleans.Graph;
using ManagedCode.Orleans.Graph.Models;
using Orleans.DurableJobs;
using GraphConstants = ManagedCode.Orleans.Graph.Interfaces.Constants;

namespace KeyLoad.Server;

internal static class NativeJobGraphRegistration
{
    private const string ExistingPolicyRequired = "The native jobs graph requires the existing default-deny policy instance.";
    internal static Type ManagerType(IServiceCollection services)
        => services.Where(static descriptor => !descriptor.IsKeyedService)
            .Select(static descriptor => descriptor.ServiceType)
            .Where(static type => !type.IsInterface && !type.IsAbstract
                && typeof(ILocalDurableJobManager).IsAssignableFrom(type)
                && type.Assembly == typeof(ILocalDurableJobManager).Assembly)
            .Distinct().Single();

    internal static void Extend(IServiceCollection services)
    {
        var nativeManager = ManagerType(services);
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
        graph.AddTransition(nativeManager.FullName!, typeof(IRequestGrain).FullName!,
            new GrainTransition(GraphConstants.AnyMethod, nameof(IRequestGrain.ExecuteStreamAsync)));
        graph.AddTransition(typeof(RecurringDueCoordinatorGrain).FullName!, typeof(IRequestGrain).FullName!,
            new GrainTransition(nameof(IDurableJobHandler.ExecuteJobAsync), nameof(IRequestGrain.ExecuteStreamAsync)));
        graph.AddTransition(typeof(RecurringDueCoordinatorGrain).FullName!, typeof(IRequestGrain).FullName!,
            new GrainTransition(nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IRequestGrain.ExecuteStreamAsync)));
        services.Remove(descriptor);
        services.AddSingleton(new GrainTransitionManager(graph, allowAllByDefault: false));
    }
}
