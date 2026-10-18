using System.Runtime.CompilerServices;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

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
        var admission = AppHostOptionsRegistration.Bind<IsolatedKeyLoadAdmissionOptions>(
            builder.Configuration.GetSection(IsolatedKeyLoadAdmissionOptions.SectionName),
            options => options.IsValid(), IsolatedKeyLoadAdmissionOptions.ValidationMessage);
        var runtime = new LoadTestsRuntimeOptions(AppHostOptionsRegistration.Get(builder), startup, resources,
            BenchmarkProvenanceRegistration.Bind(), deployment, relay, admission);
        builder.Services.AddSingleton(runtime.Startup);
        builder.Services.AddSingleton(runtime.ServerResources);
        builder.Services.AddSingleton(runtime.Provenance);
        builder.Services.AddSingleton(runtime.Deployment);
        builder.Services.AddSingleton(runtime.BenchmarkRelay);
        builder.Services.AddSingleton(runtime.IsolatedAdmission);
        return runtime;
    }

    internal static IOptions<IsolatedKeyLoadAdmissionOptions> BindDocumentAdmission(
        IOptions<IsolatedKeyLoadAdmissionOptions> admission, IOptions<BenchmarkDeploymentOptions> deployment)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(deployment);
        var policy = admission.Value;
        policy.Validate();
        var resourcePolicy = deployment.Value;
        if (!resourcePolicy.IsValid())
        {
            throw new OptionsValidationException(BenchmarkDeploymentOptions.SectionName, typeof(BenchmarkDeploymentOptions),
                [BenchmarkDeploymentOptions.ValidationMessage]);
        }
        var factory = new OptionsFactory<IsolatedKeyLoadAdmissionOptions>(
            [new ConfigureOptions<IsolatedKeyLoadAdmissionOptions>(options =>
            {
                options.DocumentWorkload = true;
                options.RequestsPerScope = resourcePolicy.DocumentKeyLoadRequestsPerScope;
                options.ReservedBytes = policy.ReservedBytes;
            })], [],
            [new ValidateOptions<IsolatedKeyLoadAdmissionOptions>(Options.DefaultName,
                options => options.IsValid(), IsolatedKeyLoadAdmissionOptions.ValidationMessage)]);
        var bound = new OptionsManager<IsolatedKeyLoadAdmissionOptions>(factory);
        _ = bound.Value;
        return bound;
    }
}
