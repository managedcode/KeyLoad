using KeyLoad.Clustering;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>The single configuration binding boundary for server and execution silo execution.</summary>
[ConfigurationBinding]
internal static class ServerRuntimeOptionsRegistration
{
    private const string InvalidCopies = "Invalid storage mode or silo/data-copy count; SharedVolume requires ReplicaCount 1.";
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
        services.AddOptions<BackgroundWorkerOptions>().Bind(configuration.GetSection(BackgroundWorkerOptions.SectionName))
            .Validate(options => options.FlushInterval > TimeSpan.Zero,
                BackgroundWorkerOptions.SectionName).ValidateOnStart();
        services.AddOptions<RuntimeDurableJobOptions>().Bind(configuration.GetSection(RuntimeDurableJobOptions.SectionName))
            .Validate(options => options.IsValid(), RuntimeDurableJobOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ReplicaCopyOptions>().Bind(configuration.GetSection(ReplicaCopyOptions.SectionName))
            .Validate(options => options.IsValid(), InvalidCopies).ValidateOnStart();
        services.AddOptions<GrainRoutingOptions>()
            .Bind(configuration.GetSection(GrainRoutingOptions.SectionName))
            .Validate(options => options.IsValid(), GrainRoutingOptions.ValidationMessage).ValidateOnStart();
    }

    private static void RegisterStorageExecution(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ZoneTreeStorageExecutionOptions>().Bind(configuration.GetSection(ZoneTreeStorageExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ZoneTreePointCacheExecutionOptions>().Bind(configuration.GetSection(ZoneTreePointCacheExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<AnnExecutionOptions>().Bind(configuration.GetSection(AnnExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), AnnExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<TextExecutionOptions>().Bind(configuration.GetSection(TextExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), TextExecutionOptions.ValidationMessage).ValidateOnStart();
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
            .PostConfigure(ConfigureNode).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NodeOptions>, NodeOptionsValidator>();
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

    internal static void ConfigureNode(NodeOptions node)
    {
        node.Validate();
    }

}
