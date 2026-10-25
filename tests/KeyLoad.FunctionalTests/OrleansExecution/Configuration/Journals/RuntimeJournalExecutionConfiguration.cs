#pragma warning disable ORLEANSEXP005
using KeyLoad.Server;
using ManagedCode.Orleans.Graph;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Metadata;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RuntimeJournalExecutionConfiguration
{
    internal static void Register(IServiceCollection services, NodeOptions _)
    {
        services.AddSerializer(serialization => serialization.AddAssembly(typeof(RuntimeJournalReplayGrain).Assembly));
        services.Configure<GrainTypeOptions>(options =>
        {
            options.AddClass(typeof(RuntimeJournalReplayGrain));
            options.AddClass(typeof(RuntimeJournalGraphCallerProbeGrain));
        });
        services.AddSingleton<IConfigureGrainTypeComponents>(provider =>
            new RuntimeJournalReplayActivatorConfiguration(provider.GetRequiredService<GrainClassMap>(), provider));
        services.AddOptions<RuntimeTestOptions>()
            .Validate(options => options.IsValid(), "The execution runtime test timing profile is invalid.")
            .ValidateOnStart();
        // DurableGrain uses the default binding of the same production journal singleton.
        services.AddSingleton(provider => provider.GetRequiredKeyedService<IJournalStorageProvider>(
            RuntimeJournalRegistration.ProviderName));
        ExtendPolicy(services);
    }

    private static void ExtendPolicy(IServiceCollection services)
    {
        var descriptor = services.Single(static service => !service.IsKeyedService
            && service.ServiceType == typeof(GrainTransitionManager));
        if (descriptor.ImplementationInstance is not GrainTransitionManager current)
        {
            throw new InvalidOperationException("The journal cases require the current default-deny policy.");
        }
        var graph = new DirectedGraph(allowSelfLoops: true);
        foreach (var edge in current.GetPolicyEdges())
        {
            foreach (var transition in edge.Transitions)
            {
                graph.AddTransition(edge.Source, edge.Target, transition);
            }
        }
        foreach (var target in new[] { typeof(IRuntimeJournalReplayGrain), typeof(IRuntimeJournalGraphCallerProbeGrain) })
        {
            graph.AddTransition(ManagedCode.Orleans.Graph.Interfaces.Constants.ClientCallerId, target.FullName!,
                new(ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod,
                    ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod));
        }
        services.Remove(descriptor);
        services.AddSingleton(new GrainTransitionManager(graph, allowAllByDefault: false));
    }
}

#pragma warning restore ORLEANSEXP005
