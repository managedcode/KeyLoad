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
        services.AddOptions<MessagingExecutionOptions>().Bind(configuration.GetSection(MessagingExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), MessagingExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<QueryExecutionOptions>().Bind(configuration.GetSection(QueryExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), QueryExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<CacheMemoryLimits>().Bind(configuration.GetSection(CacheMemoryLimits.SectionName))
            .Validate(options => { options.Validate(); return true; }, CacheMemoryLimits.ValidationMessage).ValidateOnStart();
        services.AddOptions<CacheReadPermitOptions>().Bind(configuration.GetSection(CacheReadPermitOptions.SectionName))
            .Validate(options => options.IsValid(), CacheReadPermitOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<RuntimeJournalOptions>().Bind(configuration.GetSection(RuntimeJournalOptions.SectionName))
            .Validate(options => options.IsValid(), RuntimeJournalOptions.ValidationMessage).ValidateOnStart();
        services.AddSingleton<CoreRuntimeOptions>();
    }
}
