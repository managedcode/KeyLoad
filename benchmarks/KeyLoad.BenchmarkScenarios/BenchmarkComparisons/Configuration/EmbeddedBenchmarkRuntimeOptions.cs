using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Holds the validated native snapshots borrowed by an embedded benchmark owner.</summary>
internal sealed record EmbeddedBenchmarkRuntimeOptions(IOptions<DatabaseLimits> Database,
    IOptions<DueWorkExecutionOptions> DueWork, IOptions<EventSourceExecutionOptions> EventSource,
    IOptions<MessagingExecutionOptions> Messaging, IOptions<GraphExecutionOptions> GraphExecution,
    IOptions<ChangeFeedExecutionOptions> ChangeFeedExecution, IOptions<BlobExecutionOptions> BlobExecution,
    IOptions<NativeClaimsExecutionOptions> NativeClaimsExecution,
    IOptions<TimeSeriesExecutionOptions> TimeSeriesExecution,
    IOptions<PartitionMovementCheckpointOptions> MovementCheckpoints,
    IOptions<ZoneTreeStorageExecutionOptions> Storage,
    IOptions<ZoneTreePointCacheExecutionOptions> PointCache);

/// <summary>Binds the actual generated runner's environment to native fixture policies.</summary>
[ConfigurationBinding]
internal static class EmbeddedBenchmarkRuntimeRegistration
{
    internal static EmbeddedBenchmarkRuntimeOptions Read()
    {
        _ = SerializationExecutionRegistration.Process.Value;
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
            BenchmarkScenarioOptionsRegistration.Read<BlobExecutionOptions>(configuration, BlobExecutionOptions.SectionName,
                settings => settings.IsValid(), BlobExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<NativeClaimsExecutionOptions>(configuration, NativeClaimsExecutionOptions.SectionName,
                settings => settings.IsValid(), NativeClaimsExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<TimeSeriesExecutionOptions>(configuration, TimeSeriesExecutionOptions.SectionName,
                settings => settings.IsValid(), TimeSeriesExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<PartitionMovementCheckpointOptions>(configuration, PartitionMovementCheckpointOptions.SectionName,
                settings => settings.IsValid(), PartitionMovementCheckpointOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<ZoneTreeStorageExecutionOptions>(configuration, ZoneTreeStorageExecutionOptions.SectionName,
                settings => settings.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage),
            BenchmarkScenarioOptionsRegistration.Read<ZoneTreePointCacheExecutionOptions>(configuration, ZoneTreePointCacheExecutionOptions.SectionName,
                settings => settings.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage));
    }
}
