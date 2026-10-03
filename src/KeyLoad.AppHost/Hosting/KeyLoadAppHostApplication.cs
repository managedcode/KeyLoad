using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns complete Aspire resource composition for the KeyLoad AppHost.</summary>
internal static class KeyLoadAppHostApplication
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var requested = TestSuiteSettings.Requested(args);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = args,
            DisableDashboard = requested
        });
        var tests = TestSuiteSettings.Read(builder.Configuration);
        if (requested && tests is null)
        {
            throw new InvalidOperationException("An explicitly selected test suite must not be empty.");
        }
        AddKeyLoad(builder);
        var app = builder.Build();
        if (tests is not null)
        {
            return await TestSuiteApplication.RunAsync(app, tests).ConfigureAwait(false);
        }
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync().ConfigureAwait(false);
        }
        return 0;
    }

    /// <summary>Composes the simultaneous Docker RF3 cluster and optional comparison dependencies.</summary>
    internal static void AddKeyLoad(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (TestSuiteSettings.Read(builder.Configuration) is { } tests)
        {
            TestSuiteResources.Add(builder, tests);
            return;
        }
        if (builder.Configuration[KeyLoad.Comparisons.ComparisonWorkerSelection.TargetSetting] is not null)
        {
            IsolatedBenchmarkResources.Add(builder);
            return;
        }
        var configuration = global::AppHostConfiguration.Read(builder);
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot);
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
