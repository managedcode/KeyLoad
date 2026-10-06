using KeyLoad.AppHost.Features.ClusterRouting;
using System.Runtime.CompilerServices;
using KeyLoad;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.ClusterReplication;
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
        builder.Services.AddSingleton(cluster);
        builder.Services.AddSingleton(command);
        builder.Services.AddSingleton(http);
        builder.Services.AddSingleton(replay);
        var profile = BindProfileExecution(builder.Configuration);
        builder.Services.AddSingleton(profile);
        var deployment = Bind<BenchmarkDeploymentOptions>(builder.Configuration.GetSection(BenchmarkDeploymentOptions.SectionName), value => value.IsValid(), BenchmarkDeploymentOptions.ValidationMessage);
        var relay = Bind<BenchmarkRelayOptions>(builder.Configuration, _ => true, BenchmarkDeploymentOptions.ValidationMessage);
        var control = AppHostControlOptionsRegistration.Bind(builder.Configuration, execution);
        var workload = startup.Value.BenchmarkMode && !control.Value.TargetSelected
            && string.Equals(startup.Value.BenchmarkProfile.Trim(), global::AppHostConfiguration.GeneralBenchmarkProfile, StringComparison.OrdinalIgnoreCase)
            ? KeyLoad.Comparisons.ComparisonOptions.Read(builder.Configuration) : null;
        builder.Services.AddSingleton(deployment);
        builder.Services.AddSingleton(relay);
        if (workload is not null) { builder.Services.AddSingleton(workload); }
        var probeFiles = Bind<RequestProbeFileOptions>(builder.Configuration.GetSection(RequestProbeFileOptions.SectionName), value => value.IsValid(), RequestProbeFileOptions.ValidationMessage);
        builder.Services.AddSingleton(probeFiles);
        var localImage = Bind<LocalImageOptions>(builder.Configuration, _ => true, ContainerImageExecutionOptions.ValidationMessage);
        builder.Services.AddSingleton(localImage);
        var images = Bind<ContainerImageOptions>(builder.Configuration, _ => true, ContainerImageExecutionOptions.ValidationMessage);
        var imageExecution = Bind<ContainerImageExecutionOptions>(builder.Configuration.GetSection(ContainerImageExecutionOptions.SectionName),
            options => options.IsValid(), ContainerImageExecutionOptions.ValidationMessage);
        builder.Services.AddSingleton(images);
        builder.Services.AddSingleton(imageExecution);
        var provenance = BenchmarkProvenanceRegistration.Bind();
        builder.Services.AddSingleton(control);
        builder.Services.AddSingleton(resources);
        builder.Services.AddSingleton(provenance);
        builder.Services.AddSingleton(execution);
        builder.Services.AddSingleton(startup);
        return new(execution, startup, resources, provenance, control, images, imageExecution, localImage, cluster, command, http, replay, profile, deployment, relay, workload, probeFiles);
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
        try { validate(); return true; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

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
    IOptions<AppHostControlOptions> Control, IOptions<ContainerImageOptions> Images,
    IOptions<ContainerImageExecutionOptions> ImageExecution, IOptions<LocalImageOptions> LocalImage,
    IOptions<ClusterDeploymentOptions> Cluster, IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission, IOptions<KeyLoad.Orleans.ReplicaReplayLimits> ReplayAdmission, IOptions<ClusterProfileExecutionOptions> Profile, IOptions<BenchmarkDeploymentOptions> Deployment,
    IOptions<BenchmarkRelayOptions> BenchmarkRelay, IOptions<KeyLoad.Comparisons.ComparisonOptions>? BenchmarkWorkload, IOptions<RequestProbeFileOptions> RequestProbeFiles);
