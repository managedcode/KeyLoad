using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using ManagedCode.Orleans.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Metadata;
using Orleans.Serialization;

namespace KeyLoad.RecoveryTests.Features.OrleansExecution;

[ConfigurationBinding]
internal sealed class RequestCqrsHostedProfile(TimeProvider clock, IOptions<GrainRoutingOptions> routing,
    IGrainRequestPhaseObserver? observer, IGrainRequestPurposeObserver? purposeObserver,
    bool runtimeText, bool runtimeAnn, DatabaseLimits? limits, RequestCqrsHostedClusterNode? clusterNode = null,
    Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory = null,
    IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver = null,
    Action<IServiceCollection, NodeOptions>? configureServices = null)
{
    private readonly string membershipSecret = clusterNode?.MembershipSecret
        ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(SigningKeyBytes));
    internal const string Root = "root";
    private const string Credential = "root.unit-test-credential-32-characters";
    private const string SettingPrefix = "--KeyLoad:";
    private const string Assignment = "=";
    private const string GuidFormat = "N";
    private const string SiloPrefix = "cqrs-host-";
    private const string PublicUrls = "--urls=http://127.0.0.1:0";
    private const string WarningLogLevel = "--Logging:LogLevel:Default=Warning";
    private const int SigningKeyBytes = 32;
    private const int OneSilo = 1;
    private const string DefaultDenyRequired = "The production host must register its execution default-deny grain policy.";
    internal TimeProvider Clock { get; } = clock;
    internal IOptions<GrainRoutingOptions> Routing { get; } = routing;
    internal IGrainRequestPhaseObserver? Observer { get; } = observer;
    internal IGrainRequestPurposeObserver? PurposeObserver { get; } = purposeObserver;
    internal bool RuntimeText { get; } = runtimeText;
    internal bool RuntimeAnn { get; } = runtimeAnn;
    internal RuntimeTestOptions Timing { get; } = new();
    internal RequestCqrsHostedClusterNode? ClusterNode { get; } = clusterNode;
    internal Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? LifecycleParticipantFactory { get; } = lifecycleParticipantFactory;
    internal IDatabaseAuthenticationExecutionObserver? AuthenticationExecutionObserver { get; } = authenticationExecutionObserver;

    internal static IOptions<GrainRoutingOptions> ValidateRouting(GrainRoutingOptions? configured)
    {
        var value = configured ?? new GrainRoutingOptions();
        if (!value.IsValid())
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(GrainRoutingOptions), [GrainRoutingOptions.ValidationMessage]);
        }
        return Options.Create(value);
    }

    internal string[] Arguments(string directory, int siloPort)
    {
        var identity = ClusterNode?.SiloName ?? SiloPrefix + Guid.NewGuid().ToString(GuidFormat);
        return [PublicUrls, WarningLogLevel,
            "--" + OrleansHostingOptions.SectionName + ":" + nameof(OrleansHostingOptions.SharedSecret) + Assignment + membershipSecret,
            Setting(nameof(NodeOptions.DataDirectory), directory),
            Setting(nameof(NodeOptions.SiloName), identity),
            Setting(nameof(NodeOptions.ClusterId), ClusterNode?.ClusterId ?? identity),
            Setting(nameof(NodeOptions.SiloPort), siloPort.ToString(CultureInfo.InvariantCulture)),
            Setting(nameof(NodeOptions.SiloCount), (ClusterNode?.SiloCount ?? OneSilo).ToString(CultureInfo.InvariantCulture)),
            Setting(nameof(NodeOptions.ReplicaCount), (ClusterNode?.ReplicaCount ?? OneSilo).ToString(CultureInfo.InvariantCulture)),
            Setting(nameof(NodeOptions.Incarnation), (ClusterNode?.Incarnation ?? Guid.NewGuid()).ToString(GuidFormat)),
            Setting(nameof(NodeOptions.SigningKey), ClusterNode?.SigningKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(SigningKeyBytes))),
            Setting(nameof(NodeOptions.AdminKey), Credential)];
    }

    internal void Register(IServiceCollection services, NodeOptions node)
    {
        services.Replace(ServiceDescriptor.Singleton(Clock));
        services.Replace(ServiceDescriptor.Singleton(Routing));
        var configuredLimits = limits ?? new DatabaseLimits();
        configuredLimits.Validate();
        var messaging = new MessagingExecutionOptions();
        messaging.Validate();
        services.Replace(ServiceDescriptor.Singleton(Options.Create(configuredLimits)));
        services.Replace(ServiceDescriptor.Singleton(Options.Create(messaging)));
        services.Configure<GrainCollectionOptions>(settings => settings.CollectionAge = Timing.OrdinaryCollectionAge);
        services.Configure<GrainTypeOptions>(settings => settings.Classes.Add(typeof(RequestCqrsIdentityProbeGrain)));
        services.AddSingleton<RequestCqrsCapabilityLedger>();
        services.AddSingleton<IConfigureGrainTypeComponents>(provider => new RequestCqrsGrainComponentConfigurator(
            provider.GetRequiredService<GrainClassMap>(), provider));
        services.AddSerializer(serialization => serialization.AddAssembly(typeof(RequestCqrsProbeResult).Assembly));
        ExtendProbePolicy(services);
        configureServices?.Invoke(services, node);
    }

    private static void ExtendProbePolicy(IServiceCollection services)
    {
        var descriptor = services.Single(value => !value.IsKeyedService && value.ServiceType == typeof(GrainTransitionManager));
        if (descriptor.ImplementationInstance is not GrainTransitionManager current)
        {
            throw new InvalidOperationException(DefaultDenyRequired);
        }
        var graph = new DirectedGraph(allowSelfLoops: true);
        foreach (var edge in current.GetPolicyEdges())
        {
            foreach (var transition in edge.Transitions)
            {
                graph.AddTransition(edge.Source, edge.Target, transition);
            }
        }
        graph.AddTransition(ManagedCode.Orleans.Graph.Interfaces.Constants.ClientCallerId,
            typeof(IRequestCqrsIdentityProbeGrain).FullName!, new(
                ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod,
                ManagedCode.Orleans.Graph.Interfaces.Constants.AnyMethod));
        services.Remove(descriptor);
        services.AddSingleton(new GrainTransitionManager(graph, allowAllByDefault: false));
    }

    private static string Setting(string name, string value) => SettingPrefix + name + Assignment + value;
}
