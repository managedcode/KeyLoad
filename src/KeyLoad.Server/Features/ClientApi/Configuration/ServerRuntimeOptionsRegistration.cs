using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>The single configuration binding boundary for server and native silo execution.</summary>
[ConfigurationBinding]
internal static class ServerRuntimeOptionsRegistration
{
    internal static IServiceCollection AddRuntimeOptions(this IServiceCollection services, IConfiguration configuration)
    {
        CoreRuntimeOptionsRegistration.AddCoreRuntimeOptions(services, configuration);
        services.AddDueCoordinationOptions(configuration);
        services.AddOptions<ReplicaExecutionOptions>()
            .Bind(configuration.GetSection(ReplicaExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ReplicaProtocol.InvalidLimits).ValidateOnStart();
        services.AddOptions<PeerDiscoveryOptions>().Bind(configuration.GetSection(PeerDiscoveryOptions.SectionName))
            .PostConfigure<IOptions<NodeOptions>>((options, node) => ConfigureDiscovery(options, node.Value, configuration))
            .Validate(options => options.IsValid(), PeerDiscoveryOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ReplicaTransportOptions>().Bind(configuration.GetSection(ReplicaTransportOptions.SectionName))
            .Validate(options => options.IsValid(), ReplicaTransportOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<OrleansMembershipOptions>()
            .Bind(configuration.GetSection(OrleansMembershipOptions.SectionName))
            .Validate(options => options.IsValid(), OrleansMembershipOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<GrainRoutingOptions>()
            .Bind(configuration.GetSection(GrainRoutingOptions.SectionName))
            .Validate(options => options.IsValid(), GrainRoutingOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ServerExecutionOptions>()
            .Bind(configuration.GetSection(ServerExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ServerExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<NodeOptions>().Bind(configuration.GetSection(ServerProtocol.ConfigurationSection))
            .PostConfigure(node => ConfigureNode(node, configuration)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NodeOptions>, NodeOptionsValidator>();
        services.AddOptions<ReplicaConfiguration>();
        services.AddSingleton<IOptionsFactory<ReplicaConfiguration>, ReplicaConfigurationFactory>();
        services.AddSingleton<ServerRuntimeOptions>();
        return services;
    }

    internal static IServiceCollection AddDueCoordinationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DueCoordinationOptions>()
            .Bind(configuration.GetSection(DueCoordinationOptions.SectionName))
            .Validate(options => options.IsValid(), DueCoordinationOptions.ValidationMessage).ValidateOnStart();
        return services;
    }

    private static void ConfigureNode(NodeOptions node, IConfiguration configuration)
    {
        var section = configuration.GetSection(ServerProtocol.ConfigurationSection);
        MembershipAuthoritySettingsValidator.ValidateSection(
            section.GetSection(MembershipAuthoritySettingsProtocol.Section), node.MembershipAuthority);
        node.RequestCqrsProbe = RequestCqrsProbeOptionsReader.Read(configuration,
            node.CreateReplicaConfiguration(Path.GetFullPath(node.DataDirectory)), node.AllowPrivateNetworkHttp);
    }

    private static void ConfigureDiscovery(PeerDiscoveryOptions options, NodeOptions node, IConfiguration configuration)
    {
        var legacyKey = string.Join(ConfigurationPath.KeyDelimiter, ServerProtocol.ConfigurationSection,
            nameof(NodeOptions.PeerConnectTimeoutMilliseconds));
        if (configuration[legacyKey] is null)
        { return; }
        var configuredKey = string.Join(ConfigurationPath.KeyDelimiter, PeerDiscoveryOptions.SectionName,
            nameof(PeerDiscoveryOptions.ConnectTimeout));
        var legacyTimeout = TimeSpan.FromMilliseconds(node.PeerConnectTimeoutMilliseconds);
        if (configuration[configuredKey] is not null && options.ConnectTimeout != legacyTimeout)
        { throw new OptionsValidationException(Options.DefaultName, typeof(PeerDiscoveryOptions), [PeerDiscoveryOptions.ValidationMessage]); }
        options.ConnectTimeout = legacyTimeout;
    }
}
