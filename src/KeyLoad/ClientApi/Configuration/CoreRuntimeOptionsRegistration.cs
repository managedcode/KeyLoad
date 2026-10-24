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
        services.AddOptions<GraphExecutionOptions>().Bind(configuration.GetSection(GraphExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), GraphExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<QueryExecutionOptions>().Bind(configuration.GetSection(QueryExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), QueryExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<CacheMemoryLimits>().Bind(configuration.GetSection(CacheMemoryLimits.SectionName))
            .Validate(options => { options.Validate(); return true; }, CacheMemoryLimits.ValidationMessage).ValidateOnStart();
        services.AddOptions<CacheReadPermitOptions>().Bind(configuration.GetSection(CacheReadPermitOptions.SectionName))
            .Validate(options => options.IsValid(), CacheReadPermitOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<RuntimeJournalOptions>().Bind(configuration.GetSection(RuntimeJournalOptions.SectionName))
            .Validate(options => options.IsValid(), RuntimeJournalOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ChangeFeedExecutionOptions>().Bind(configuration.GetSection(ChangeFeedExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ChangeFeedExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<TimeSeriesExecutionOptions>().Bind(configuration.GetSection(TimeSeriesExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), TimeSeriesExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ClaimsExecutionOptions>().Bind(configuration.GetSection(ClaimsExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ClaimsExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<KeyLoad.Query.Search.PackedAnnOptions>().Bind(configuration.GetSection(KeyLoad.Query.Search.PackedAnnOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoad.Query.Search.PackedAnnOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<KeyLoad.Query.Search.PackedAnnStorageOptions>().Bind(configuration.GetSection(KeyLoad.Query.Search.PackedAnnStorageOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoad.Query.Search.PackedAnnStorageOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<KeyLoad.Core.Search.AnnSeedOptions>().Bind(configuration.GetSection(KeyLoad.Core.Search.AnnSeedOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoad.Core.Search.AnnSeedOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<AnnMaintenanceOptions>().Bind(configuration.GetSection(AnnMaintenanceOptions.SectionName))
            .Validate(options => options.IsValid(), AnnMaintenanceOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<TextIndexMaintenanceOptions>().Bind(configuration.GetSection(TextIndexMaintenanceOptions.SectionName))
            .Validate(options => options.IsValid(), TextIndexMaintenanceOptions.ValidationMessage).ValidateOnStart();
        services.AddSingleton<CoreRuntimeOptions>();
    }
}
