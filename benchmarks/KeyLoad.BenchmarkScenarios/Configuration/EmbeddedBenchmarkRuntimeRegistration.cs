using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Binds the actual generated runner's environment to execution fixture policies.</summary>
[ConfigurationBinding]
internal static class EmbeddedBenchmarkRuntimeRegistration
{
    internal static EmbeddedBenchmarkRuntimeOptions Read()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        return new(
            BenchmarkScenarioOptionsRegistration.Read<DatabaseLimits>(configuration, DatabaseLimits.SectionName,
                settings => settings.IsValid(), DatabaseLimits.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<DueWorkExecutionOptions>(configuration, DueWorkExecutionOptions.SectionName,
                settings => settings.IsValid(), DueWorkExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<EventSourceExecutionOptions>(configuration, EventSourceExecutionOptions.SectionName,
                settings => settings.IsValid(), EventSourceExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<MessagingExecutionOptions>(configuration, MessagingExecutionOptions.SectionName,
                settings => settings.IsValid(), MessagingExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<GraphExecutionOptions>(configuration, GraphExecutionOptions.SectionName,
                settings => settings.IsValid(), GraphExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<ChangeFeedExecutionOptions>(configuration, ChangeFeedExecutionOptions.SectionName,
                settings => settings.IsValid(), ChangeFeedExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<ClaimsExecutionOptions>(configuration, ClaimsExecutionOptions.SectionName,
                settings => settings.IsValid(), ClaimsExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<TimeSeriesExecutionOptions>(configuration, TimeSeriesExecutionOptions.SectionName,
                settings => settings.IsValid(), TimeSeriesExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<ZoneTreeStorageExecutionOptions>(configuration, ZoneTreeStorageExecutionOptions.SectionName,
                settings => settings.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<ZoneTreePointCacheExecutionOptions>(configuration, ZoneTreePointCacheExecutionOptions.SectionName,
                settings => settings.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage));
    }
}
