using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.ClusterReplication.Commands;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns complete Aspire resource composition for the KeyLoad AppHost.</summary>
internal static class KeyLoadAppHostApplication
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (ClusterProfileUpgradeCommand.Dispatch(args) is { } upgradeExitCode)
        { return upgradeExitCode; }
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
        var twoRf3 = TwoRf3Profile.ValidateAndRead(builder.Configuration);
        ProtocolCohortImages.ValidateMode(builder.Configuration);
        RequestCqrsProbeProfile.ValidateMode(builder.Configuration);
        var tests = TestSuiteSettings.Read(builder.Configuration);
        var scaleSelected = builder.Configuration[KeyLoad.Comparisons.ComparisonWorkerSelection.ScaleProfileSetting] is not null;
        if (scaleSelected && (tests is not null
            || builder.Configuration[KeyLoad.Comparisons.ComparisonWorkerSelection.TargetSetting] is null))
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
        if (builder.Configuration[KeyLoad.Comparisons.ComparisonWorkerSelection.TargetSetting] is not null)
        {
            IsolatedBenchmarkResources.Add(builder);
            return;
        }
        var configuration = global::AppHostConfiguration.Read(builder);
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot);
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
