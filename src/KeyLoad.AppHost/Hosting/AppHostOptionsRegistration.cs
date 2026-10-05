using System.Runtime.CompilerServices;
using KeyLoad;
using KeyLoad.AppHost.Features.TestInfrastructure;
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
        builder.Services.AddSingleton(execution);
        builder.Services.AddSingleton(startup);
        return new(execution, startup);
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

internal sealed record AppHostRuntimeOptions(IOptions<TestExecutionOptions> TestExecution, IOptions<AppHostStartupOptions> Startup);
