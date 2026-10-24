using System.Runtime.CompilerServices;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Owns comparison deployment options independently of the functional application host.</summary>
[ConfigurationBinding]
internal static class LoadTestsOptionsRegistration
{
    private static readonly ConditionalWeakTable<IDistributedApplicationBuilder, LoadTestsRuntimeOptions> BoundOptions = new();

    internal static LoadTestsRuntimeOptions Get(IDistributedApplicationBuilder builder) => BoundOptions.GetValue(builder, Bind);

    private static LoadTestsRuntimeOptions Bind(IDistributedApplicationBuilder builder)
    {
        var startup = AppHostOptionsRegistration.Bind<LoadTestsStartupOptions>(builder.Configuration,
            _ => true, BenchmarkDeploymentOptions.ValidationMessage);
        var resources = AppHostOptionsRegistration.Bind<ScaleServerResourceOptions>(
            builder.Configuration.GetSection(ScaleServerResourceOptions.SectionName),
            options => options.IsValid(), ScaleServerResourceOptions.ValidationMessage);
        var deployment = AppHostOptionsRegistration.Bind<BenchmarkDeploymentOptions>(
            builder.Configuration.GetSection(BenchmarkDeploymentOptions.SectionName),
            options => options.IsValid(), BenchmarkDeploymentOptions.ValidationMessage);
        var relay = AppHostOptionsRegistration.Bind<BenchmarkRelayOptions>(builder.Configuration,
            _ => true, BenchmarkDeploymentOptions.ValidationMessage);
        var runtime = new LoadTestsRuntimeOptions(AppHostOptionsRegistration.Get(builder), startup, resources,
            BenchmarkProvenanceRegistration.Bind(), deployment, relay);
        builder.Services.AddSingleton(runtime.Startup);
        builder.Services.AddSingleton(runtime.ServerResources);
        builder.Services.AddSingleton(runtime.Provenance);
        builder.Services.AddSingleton(runtime.Deployment);
        builder.Services.AddSingleton(runtime.BenchmarkRelay);
        return runtime;
    }

}
