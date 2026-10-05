using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.Server;

/// <summary>Binds and validates operation policy at the central server configuration boundary.</summary>
[ConfigurationBinding]
internal static class CoreRuntimeOptionsRegistration
{
    internal static void AddCoreRuntimeOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseLimits>().Bind(configuration.GetSection(DatabaseLimits.SectionName))
            .Validate(options => options.IsValid(), DatabaseLimits.ValidationMessage).ValidateOnStart();
        services.AddOptions<DueWorkExecutionOptions>().Bind(configuration.GetSection(DueWorkExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), DueWorkExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<EventSourceExecutionOptions>().Bind(configuration.GetSection(EventSourceExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), EventSourceExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<QueryExecutionOptions>().Bind(configuration.GetSection(QueryExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), QueryExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddSingleton<CoreRuntimeOptions>();
    }
}
