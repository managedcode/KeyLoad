using System.Runtime.CompilerServices;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.CodeQuality;
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
        var resources = Bind<ScaleServerResourceOptions>(builder.Configuration.GetSection(ScaleServerResourceOptions.SectionName),
            options => options.IsValid(), ScaleServerResourceOptions.ValidationMessage);
        var cluster = Bind<ClusterDeploymentOptions>(builder.Configuration, value => value.IsValid(), ClusterDeploymentOptions.ValidationMessage);
        var command = Bind<CommandAdmissionLimits>(builder.Configuration.GetSection(CommandAdmissionLimits.SectionName), value => Validate(value.Validate), CommandAdmissionLimits.ValidationMessage);
        var http = Bind<HttpAdmissionLimits>(builder.Configuration.GetSection(HttpAdmissionLimits.SectionName), value => Validate(value.Validate), HttpAdmissionLimits.ValidationMessage);
        var replay = Bind<KeyLoad.Orleans.ReplicaReplayLimits>(builder.Configuration.GetSection(ClusterDeploymentOptions.ReplaySection), value => Validate(() => value.Validate(ClusterDeploymentOptions.VoterCount)), ClusterDeploymentOptions.ValidationMessage);
        var profile = BindProfileExecution(builder.Configuration);
        var deployment = Bind<BenchmarkDeploymentOptions>(builder.Configuration.GetSection(BenchmarkDeploymentOptions.SectionName), value => value.IsValid(), BenchmarkDeploymentOptions.ValidationMessage);
        var relay = Bind<BenchmarkRelayOptions>(builder.Configuration, _ => true, BenchmarkDeploymentOptions.ValidationMessage);
        var control = AppHostControlOptionsRegistration.Bind(builder.Configuration, execution);
        var workload = startup.Value.BenchmarkMode && !control.Value.TargetSelected
            && string.Equals(startup.Value.BenchmarkProfile.Trim(), global::AppHostConfiguration.GeneralBenchmarkProfile, StringComparison.OrdinalIgnoreCase)
            ? KeyLoad.Comparisons.ComparisonOptions.Read(builder.Configuration) : null;
        var probeFiles = Bind<RequestProbeFileOptions>(builder.Configuration.GetSection(RequestProbeFileOptions.SectionName), value => value.IsValid(), RequestProbeFileOptions.ValidationMessage);
        var localImage = Bind<LocalImageOptions>(builder.Configuration, _ => true, ContainerImageExecutionOptions.ValidationMessage);
        var images = Bind<ContainerImageOptions>(builder.Configuration, _ => true, ContainerImageExecutionOptions.ValidationMessage);
        var imageExecution = Bind<ContainerImageExecutionOptions>(builder.Configuration.GetSection(ContainerImageExecutionOptions.SectionName),
            options => options.IsValid(), ContainerImageExecutionOptions.ValidationMessage);
        var provenance = BenchmarkProvenanceRegistration.Bind();
        var isolatedReplay = IsolatedKeyLoadReplayOptionsRegistration.Bind(builder.Configuration);
        var isolatedAdmission = Bind<KeyLoad.Comparisons.IsolatedKeyLoadAdmissionOptions>(
            builder.Configuration.GetSection(KeyLoad.Comparisons.IsolatedKeyLoadAdmissionOptions.SectionName),
            value => value.IsValid(), KeyLoad.Comparisons.IsolatedKeyLoadAdmissionOptions.ValidationMessage);
        var coverage = Bind<NativeCoverageExecutionOptions>(builder.Configuration.GetSection(NativeCoverageExecutionOptions.SectionName),
            value => value.IsValid(), NativeCoverageExecutionOptions.ValidationMessage);
        var runtime = new AppHostRuntimeOptions(execution, startup, resources, provenance, control, images, imageExecution, localImage,
            cluster, command, http, replay, profile, deployment, relay, workload, probeFiles, isolatedReplay, isolatedAdmission, coverage);
        Register(builder.Services, runtime);
        return runtime;
    }

    private static void Register(IServiceCollection services, AppHostRuntimeOptions runtime)
    {
        services.AddSingleton(runtime.Cluster);
        services.AddSingleton(runtime.CommandAdmission);
        services.AddSingleton(runtime.HttpAdmission);
        services.AddSingleton(runtime.ReplayAdmission);
        services.AddSingleton(runtime.Profile);
        services.AddSingleton(runtime.Deployment);
        services.AddSingleton(runtime.BenchmarkRelay);
        if (runtime.BenchmarkWorkload is { } workload)
        { services.AddSingleton(workload); }
        services.AddSingleton(runtime.RequestProbeFiles);
        services.AddSingleton(runtime.LocalImage);
        services.AddSingleton(runtime.Images);
        services.AddSingleton(runtime.ImageExecution);
        services.AddSingleton(runtime.Control);
        services.AddSingleton(runtime.ServerResources);
        services.AddSingleton(runtime.Provenance);
        services.AddSingleton(runtime.TestExecution);
        services.AddSingleton(runtime.Startup);
        services.AddSingleton(runtime.IsolatedAdmission);
        services.AddSingleton(runtime.NativeCoverage);
    }

    internal static IOptions<ClusterProfileExecutionOptions> BindProfileExecution(IConfiguration configuration) =>
        Bind<ClusterProfileExecutionOptions>(configuration.GetSection(ClusterProfileExecutionOptions.SectionName),
            value => value.IsValid(), ClusterProfileExecutionOptions.ValidationMessage);

    internal static IOptions<TestExecutionOptions> BindTestExecution(IConfiguration configuration) =>
        Bind<TestExecutionOptions>(configuration.GetSection(TestExecutionOptions.SectionName),
            options => options.IsValid(), TestExecutionOptions.ValidationMessage);

    internal static IOptions<TestBootstrapOptions> BindTestBootstrap(IConfiguration configuration) =>
        Bind<TestBootstrapOptions>(configuration, _ => true, TestExecutionOptions.ValidationMessage);

    private static bool Validate(Action validate)
    {
        try
        { validate(); return true; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static OptionsManager<T> Bind<T>(IConfiguration configuration, Func<T, bool> predicate, string message) where T : class, new()
    {
        var factory = new OptionsFactory<T>([new ConfigureFromConfigurationOptions<T>(configuration)], [],
            [new ValidateOptions<T>(Options.DefaultName, predicate, message)]);
        var options = new OptionsManager<T>(factory);
        _ = options.Value;
        return options;
    }
}

internal sealed record AppHostRuntimeOptions(IOptions<TestExecutionOptions> TestExecution, IOptions<AppHostStartupOptions> Startup,
    IOptions<ScaleServerResourceOptions> ServerResources, IOptions<BenchmarkProvenanceOptions> Provenance,
    IOptions<AppHostControlOptions> Control, IOptions<ContainerImageOptions> Images,
    IOptions<ContainerImageExecutionOptions> ImageExecution, IOptions<LocalImageOptions> LocalImage,
    IOptions<ClusterDeploymentOptions> Cluster, IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission, IOptions<KeyLoad.Orleans.ReplicaReplayLimits> ReplayAdmission, IOptions<ClusterProfileExecutionOptions> Profile, IOptions<BenchmarkDeploymentOptions> Deployment,
    IOptions<BenchmarkRelayOptions> BenchmarkRelay, IOptions<KeyLoad.Comparisons.ComparisonOptions>? BenchmarkWorkload, IOptions<RequestProbeFileOptions> RequestProbeFiles,
    IOptions<KeyLoad.Orleans.ReplicaReplayLimits> IsolatedReplayAdmission,
    IOptions<KeyLoad.Comparisons.IsolatedKeyLoadAdmissionOptions> IsolatedAdmission,
    IOptions<NativeCoverageExecutionOptions> NativeCoverage);
