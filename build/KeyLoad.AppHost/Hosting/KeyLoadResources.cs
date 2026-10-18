using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Composes KeyLoad resources for the process and native fixture hosts.</summary>
[ConfigurationBinding]
internal static class KeyLoadResources
{
    /// <summary>Composes the simultaneous Docker CLUSTER cluster and optional comparison dependencies.</summary>
    internal static void AddKeyLoad(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        var runtimeOptions = AppHostOptionsRegistration.Get(builder);
        var tests = runtimeOptions.Control.Value.Tests;
        if (tests is not null)
        {
            TestSuiteResources.Add(builder, tests);
            return;
        }
        if (runtimeOptions.Control.Value.TargetSelected)
        {
            IsolatedBenchmarkResources.Add(builder);
            return;
        }
        var configuration = global::AppHostConfiguration.Read(builder);
        if (configuration.BenchmarkMode)
        {
            throw new InvalidOperationException(BenchmarkSessionResourceOptions.Profile);
        }
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot, AppHostOptionsRegistration.Get(builder).Profile);
        _ = global::ClusterResources.Add(builder, profile, configuration.DataRoot, configuration.Ephemeral,
            functionalNodeCount: runtimeOptions.Control.Value.FunctionalNodeCount);
    }
}
