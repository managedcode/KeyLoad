using System.Runtime.CompilerServices;
using KeyLoad;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns native typed options before Aspire build-time resource selection and shares them with runtime DI.</summary>
[ConfigurationBinding]
internal static class AppHostOptionsRegistration
{
    private static readonly ConditionalWeakTable<IDistributedApplicationBuilder, AppHostRuntimeOptions> BoundOptions = new();

    internal static AppHostRuntimeOptions Get(IDistributedApplicationBuilder builder) => BoundOptions.GetValue(builder, Bind);

    private static AppHostRuntimeOptions Bind(IDistributedApplicationBuilder builder)
    {
        var execution = Bind<TestExecutionOptions>(builder.Configuration.GetSection(TestExecutionOptions.SectionName),
            options => options.IsValid(), TestExecutionOptions.ValidationMessage);
        var startup = Bind<AppHostStartupOptions>(builder.Configuration, options => options.IsValid(),
            AppHostStartupOptions.ValidationMessage);
        var resources = Bind<ScaleServerResourceOptions>(builder.Configuration.GetSection(ScaleServerResourceOptions.SectionName),
            options => options.IsValid(), ScaleServerResourceOptions.ValidationMessage);
        var provenance = BenchmarkProvenanceRegistration.Bind();
        var control = AppHostControlOptionsRegistration.Bind(builder.Configuration, execution);
        builder.Services.AddSingleton(control);
        builder.Services.AddSingleton(resources);
        builder.Services.AddSingleton(provenance);
        builder.Services.AddSingleton(execution);
        builder.Services.AddSingleton(startup);
        return new(execution, startup, resources, provenance, control);
    }

    internal static IOptions<TestExecutionOptions> BindTestExecution(IConfiguration configuration) =>
        Bind<TestExecutionOptions>(configuration.GetSection(TestExecutionOptions.SectionName),
            options => options.IsValid(), TestExecutionOptions.ValidationMessage);

    private static IOptions<T> Bind<T>(IConfiguration configuration, Func<T, bool> predicate, string message) where T : class, new()
    {
        var factory = new OptionsFactory<T>([new ConfigureFromConfigurationOptions<T>(configuration)], [],
            [new ValidateOptions<T>(Options.DefaultName, predicate, message)]);
        IOptions<T> options = new OptionsManager<T>(factory);
        _ = options.Value;
        return options;
    }
}

internal sealed record AppHostRuntimeOptions(IOptions<TestExecutionOptions> TestExecution, IOptions<AppHostStartupOptions> Startup,
    IOptions<ScaleServerResourceOptions> ServerResources, IOptions<BenchmarkProvenanceOptions> Provenance,
    IOptions<AppHostControlOptions> Control);
