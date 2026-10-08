using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>The single configuration binding boundary for server and native silo execution.</summary>
[ConfigurationBinding]
internal static class ServerRuntimeOptionsRegistration
{
    internal static IServiceCollection AddRuntimeOptions(this IServiceCollection services, IConfiguration configuration)
    {
        CoreRuntimeOptionsRegistration.AddCoreRuntimeOptions(services, configuration);
        RegisterClusterExecution(services, configuration);
        RegisterStorageExecution(services, configuration);
        RegisterHostExecution(services, configuration);
        RegisterNodeProjections(services, configuration);
        services.AddSingleton<ServerRuntimeOptions>();
        return services;
    }

    private static void RegisterClusterExecution(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDueCoordinationOptions(configuration);
        services.AddOptions<NativeDurableJobOptions>().Bind(configuration.GetSection(NativeDurableJobOptions.SectionName))
            .Validate(options => options.IsValid(), NativeDurableJobOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ReplicaExecutionOptions>()
            .Bind(configuration.GetSection(ReplicaExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ReplicaProtocol.InvalidLimits).ValidateOnStart();
        services.AddOptions<PeerDiscoveryOptions>().Bind(configuration.GetSection(PeerDiscoveryOptions.SectionName))
            .Validate(options => options.IsValid(), PeerDiscoveryOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ReplicaTransportOptions>().Bind(configuration.GetSection(ReplicaTransportOptions.SectionName))
            .Validate(options => options.IsValid(), ReplicaTransportOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<OrleansMembershipOptions>()
            .Bind(configuration.GetSection(OrleansMembershipOptions.SectionName))
            .Validate(options => options.IsValid(), OrleansMembershipOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<GrainRoutingOptions>()
            .Bind(configuration.GetSection(GrainRoutingOptions.SectionName))
            .Validate(options => options.IsValid(), GrainRoutingOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<RemoteDocumentExecutionOptions>()
            .Bind(configuration.GetSection(RemoteDocumentExecutionOptions.SectionName), binding => binding.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), RemoteDocumentExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<PhysicalOwnerExecutionOptions>()
            .Bind(configuration.GetSection(PhysicalOwnerExecutionOptions.SectionName), binding => binding.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), PhysicalOwnerExecutionOptions.ValidationMessage).ValidateOnStart();
    }

    private static void RegisterStorageExecution(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ZoneTreeStorageExecutionOptions>().Bind(configuration.GetSection(ZoneTreeStorageExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ZoneTreePointCacheExecutionOptions>().Bind(configuration.GetSection(ZoneTreePointCacheExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<RequestProbeExecutionOptions>().Bind(configuration.GetSection(RequestProbeExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), RequestProbeExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<NativeAnnExecutionOptions>().Bind(configuration.GetSection(NativeAnnExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), NativeAnnExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<NativeTextExecutionOptions>().Bind(configuration.GetSection(NativeTextExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), NativeTextExecutionOptions.ValidationMessage).ValidateOnStart();
    }

    private static void RegisterHostExecution(IServiceCollection services, IConfiguration configuration)
    {
        AddDatabasePhaseOptions(services, configuration);
        services.AddOptions<ServerExecutionOptions>()
            .Bind(configuration.GetSection(ServerExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ServerExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<McpExecutionOptions>().Bind(configuration.GetSection(McpExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), McpExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<AdminObservationOptions>().Bind(configuration.GetSection(AdminObservationOptions.SectionName))
            .Validate(options => options.IsValid(), AdminObservationOptions.ValidationMessage).ValidateOnStart();
    }

    internal static void AddDatabasePhaseOptions(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DatabasePhaseExecutionOptions.SectionName);
        if (section.Value is not null)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(DatabasePhaseExecutionOptions),
                [DatabasePhaseExecutionOptions.ValidationMessage]);
        }
        services.AddOptions<DatabasePhaseExecutionOptions>()
            .Bind(section, binding => binding.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), DatabasePhaseExecutionOptions.ValidationMessage).ValidateOnStart();
    }

    private static void RegisterNodeProjections(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NodeOptions>().Bind(configuration.GetSection(ServerProtocol.ConfigurationSection))
            .PostConfigure(node => ConfigureNode(node, configuration)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NodeOptions>, NodeOptionsValidator>();
        services.AddOptions<ReplicaConfiguration>();
        services.AddSingleton<IOptionsFactory<ReplicaConfiguration>, ReplicaConfigurationFactory>();
        services.AddOptions<ReplicaReplayLimits>();
        services.AddSingleton<IOptionsFactory<ReplicaReplayLimits>, ReplicaReplayOptionsFactory>();
        services.AddOptions<ReplicaPeerOptions>();
        services.AddSingleton<IOptionsFactory<ReplicaPeerOptions>, ReplicaPeerOptionsFactory>();
        services.AddOptions<CommandAdmissionLimits>();
        services.AddSingleton<IOptionsFactory<CommandAdmissionLimits>>(provider =>
            new NodeOptionsProjectionFactory<CommandAdmissionLimits>(provider.GetRequiredService<IOptions<NodeOptions>>(),
                node => node.CommandAdmission));
        services.AddOptions<HttpAdmissionLimits>();
        services.AddSingleton<IOptionsFactory<HttpAdmissionLimits>>(provider =>
            new NodeOptionsProjectionFactory<HttpAdmissionLimits>(provider.GetRequiredService<IOptions<NodeOptions>>(),
                node => node.HttpAdmission));
        services.AddOptions<McpMemoryLimits>();
        services.AddSingleton<IOptionsFactory<McpMemoryLimits>>(provider =>
            new NodeOptionsProjectionFactory<McpMemoryLimits>(provider.GetRequiredService<IOptions<NodeOptions>>(),
                node => node.McpMemory));
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

}
