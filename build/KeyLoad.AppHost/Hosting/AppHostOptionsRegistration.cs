using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using KeyLoad.AppHost.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns execution typed options before Aspire build-time resource selection and shares them with runtime DI.</summary>
[ConfigurationBinding]
internal static class AppHostOptionsRegistration
{
    private static readonly ConditionalWeakTable<IDistributedApplicationBuilder, AppHostRuntimeOptions> BoundOptions = new();

    internal static AppHostRuntimeOptions Get(IDistributedApplicationBuilder builder) => BoundOptions.GetValue(builder, Bind);

    private static AppHostRuntimeOptions Bind(IDistributedApplicationBuilder builder)
    {
        var execution = BindTestExecution(builder.Configuration);
        var startup = Bind<AppHostStartupOptions>(builder.Configuration, options => options.IsValid(),
            AppHostStartupOptions.ValidationMessage);
        var cluster = Bind<ClusterDeploymentOptions>(builder.Configuration, value => value.IsValid(), ClusterDeploymentOptions.ValidationMessage);
        var command = Bind<CommandAdmissionLimits>(builder.Configuration.GetSection(CommandAdmissionLimits.SectionName), value => Validate(value.Validate), CommandAdmissionLimits.ValidationMessage);
        var http = Bind<HttpAdmissionLimits>(builder.Configuration.GetSection(HttpAdmissionLimits.SectionName), value => Validate(value.Validate), HttpAdmissionLimits.ValidationMessage);
        var control = AppHostControlOptionsRegistration.Bind(builder.Configuration);
        var runtime = new AppHostRuntimeOptions(execution, startup, control, cluster, command, http);
        Register(builder.Services, runtime);
        return runtime;
    }

    private static void Register(IServiceCollection services, AppHostRuntimeOptions runtime)
    {
        services.AddSingleton(runtime.Cluster);
        services.AddSingleton(runtime.CommandAdmission);
        services.AddSingleton(runtime.HttpAdmission);
        services.AddSingleton(runtime.Control);
        services.AddSingleton(runtime.TestExecution);
        services.AddSingleton(runtime.Startup);
    }

    internal static ClusterIdentity CreateIdentity(IDistributedApplicationBuilder builder, bool ephemeral)
        => ephemeral ? new(Guid.NewGuid(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(ClusterIdentity.SecretBytes)),
            ClusterIdentity.AdminPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(ClusterIdentity.SecretBytes)))
            : BindIdentity(builder.Configuration).Value;

    internal static IOptions<ClusterIdentity> BindIdentity(IConfiguration configuration) =>
        Bind<ClusterIdentity>(configuration.GetSection(ClusterIdentity.SectionName),
            identity => identity.IsValid(), ClusterIdentity.InvalidIdentity);

    internal static IOptions<TestExecutionOptions> BindTestExecution(IConfiguration configuration) =>
        Bind<TestExecutionOptions>(configuration.GetSection(TestExecutionOptions.SectionName),
            options => options.IsValid(), TestExecutionOptions.ValidationMessage);

    private static bool Validate(Action validate)
    {
        try
        {
            validate();
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal static OptionsManager<T> Bind<T>(IConfiguration configuration, Func<T, bool> predicate, string message) where T : class, new()
    {
        var factory = new OptionsFactory<T>([new ConfigureFromConfigurationOptions<T>(configuration)], [],
            [new ValidateOptions<T>(Options.DefaultName, predicate, message)]);
        var options = new OptionsManager<T>(factory);
        _ = options.Value;
        return options;
    }
}
