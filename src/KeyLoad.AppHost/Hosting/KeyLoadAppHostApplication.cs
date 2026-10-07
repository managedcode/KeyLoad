using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns complete Aspire resource composition for the KeyLoad AppHost.</summary>
[ConfigurationBinding]
internal static class KeyLoadAppHostApplication
{
    internal static async Task<int> RunAsync(string[] args)
    {
        const string MessageText = "An explicitly selected test suite must not be empty.";
        const int EmptyResult = 0;

        _ = SerializationExecutionRegistration.Process.Value;
        var requested = TestSuiteSettings.Requested(args);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = args,
            DisableDashboard = requested
        });
        var runtimeOptions = AppHostOptionsRegistration.Get(builder);
        var tests = runtimeOptions.Control.Value.Tests;
        var loggerModelControl = runtimeOptions.Control.Value.LoggerModelControl;
        if (requested && tests is null)
        {
            throw new InvalidOperationException(MessageText);
        }
        AddKeyLoad(builder);
        var app = builder.Build();
        if (loggerModelControl)
        { return EmptyResult; }
        if (tests is not null)
        {
            return await TestSuiteApplication.RunAsync(app, tests).ConfigureAwait(false);
        }
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync().ConfigureAwait(false);
        }
        return EmptyResult;
    }

    /// <summary>Composes the simultaneous Docker RF3 cluster and optional comparison dependencies.</summary>
    internal static void AddKeyLoad(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        var runtimeOptions = AppHostOptionsRegistration.Get(builder);
        var twoRf3 = runtimeOptions.Control.Value.TwoRf3;
        var tests = runtimeOptions.Control.Value.Tests;
        var scaleSelected = runtimeOptions.Control.Value.ScaleSelected;
        if (scaleSelected && (tests is not null
            || !runtimeOptions.Control.Value.TargetSelected))
        {
            throw new InvalidOperationException(KeyLoad.Comparisons.ComparisonWorkerSelection.InvalidSelection);
        }
        if (tests is not null)
        {
            if (twoRf3)
            { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
            TestSuiteResources.Add(builder, tests);
            return;
        }
        if (runtimeOptions.Control.Value.TargetSelected)
        {
            IsolatedBenchmarkResources.Add(builder);
            return;
        }
        var configuration = global::AppHostConfiguration.Read(builder);
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot, AppHostOptionsRegistration.Get(builder).Profile);
        if (twoRf3)
        {
            _ = TwoRf3ClusterResources.Add(builder, profile, configuration.DataRoot);
            return;
        }
        var nodes = global::ClusterResources.Add(builder, profile, configuration.DataRoot, configuration.Ephemeral);
        if (configuration.BenchmarkMode)
        {
            var admin = builder.CreateResourceBuilder(builder.Resources.OfType<ParameterResource>()
                .Single(resource => resource.Name == global::AppHostConfiguration.AdminParameter));
            if (string.Equals(configuration.BenchmarkProfile, global::AppHostConfiguration.TimeSeriesBenchmarkProfile,
                    StringComparison.OrdinalIgnoreCase))
            {
                TimeSeriesBenchmarkResources.Add(builder, nodes, admin, configuration.BenchmarkRoot);
            }
            else
            {
                global::BenchmarkResources.Add(builder, nodes, admin, configuration.BenchmarkRoot);
            }
        }
    }
}
