using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.Server;

/// <summary>Binds and validates operation policy at the central server configuration boundary.</summary>
[ConfigurationBinding]
internal static class CoreRuntimeOptionsRegistration
{
    internal static void AddCoreRuntimeOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(SerializationExecutionRegistration.Process);
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
        services.AddOptions<CommandInboxExecutionOptions>().Bind(configuration.GetSection(CommandInboxExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), CommandInboxExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<ChangeFeedExecutionOptions>().Bind(configuration.GetSection(ChangeFeedExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), ChangeFeedExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<TimeSeriesExecutionOptions>().Bind(configuration.GetSection(TimeSeriesExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), TimeSeriesExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<BlobExecutionOptions>().Bind(configuration.GetSection(BlobExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), BlobExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<NativeClaimsExecutionOptions>().Bind(configuration.GetSection(NativeClaimsExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), NativeClaimsExecutionOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<KeyLoad.Query.Features.Search.PackedAnnOptions>().Bind(configuration.GetSection(KeyLoad.Query.Features.Search.PackedAnnOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoad.Query.Features.Search.PackedAnnOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<KeyLoad.Query.Features.Search.PackedAnnStorageOptions>().Bind(configuration.GetSection(KeyLoad.Query.Features.Search.PackedAnnStorageOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoad.Query.Features.Search.PackedAnnStorageOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<KeyLoad.Core.Features.Search.AnnSeedOptions>().Bind(configuration.GetSection(KeyLoad.Core.Features.Search.AnnSeedOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoad.Core.Features.Search.AnnSeedOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<AnnMaintenanceOptions>().Bind(configuration.GetSection(AnnMaintenanceOptions.SectionName))
            .Validate(options => options.IsValid(), AnnMaintenanceOptions.ValidationMessage).ValidateOnStart();
        services.AddOptions<TextIndexMaintenanceOptions>().Bind(configuration.GetSection(TextIndexMaintenanceOptions.SectionName))
            .Validate(options => options.IsValid(), TextIndexMaintenanceOptions.ValidationMessage).ValidateOnStart();
        services.AddSingleton<CoreRuntimeOptions>();
    }
}
