using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Composes only native KeyLoad project processes and Orleans membership storage.</summary>
internal static class KeyLoadResources
{
    private const string InvalidMode = "Comparison workloads belong to KeyLoad.LoadTests.AppHost; native tests use their TUnit entry point.";

    public static void AddKeyLoad(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(TimeProvider.System);
        var runtime = AppHostOptionsRegistration.Get(builder);
        var control = runtime.Control.Value;
        if (control.TargetSelected || control.BenchmarksEnabled || control.ComparisonSelectorsPresent)
        {
            throw new InvalidOperationException(InvalidMode);
        }
        var configuration = global::AppHostConfiguration.Read(builder);
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot, runtime.Profile);
        global::ClusterResources.Add(builder, profile, configuration.DataRoot, configuration.Ephemeral,
            functionalNodeCount: control.FunctionalNodeCount);
    }
}
