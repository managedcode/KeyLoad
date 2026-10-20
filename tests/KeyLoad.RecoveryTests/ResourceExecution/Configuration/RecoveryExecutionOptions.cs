using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests;

/// <summary>Explicit validated execution options composition for genuine test-owned engine and replica fixtures.</summary>
[ConfigurationBinding]
internal static class RecoveryExecutionOptions
{
    internal static IOptions<RecoveryTrialOptions> TrialPolicy { get; } = Trial();

    private static IOptions<RecoveryTrialOptions> Trial()
    {
        var settings = new RecoveryTrialOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ProcessReadinessOptions> ProcessReadiness()
    {
        var settings = new ProcessReadinessOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<TextExecutionOptions> RuntimeText()
    {
        var settings = new TextExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<MessagingExecutionOptions> Messaging()
    {
        var settings = new MessagingExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<GraphExecutionOptions> GraphExecution()
    {
        var settings = new GraphExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ChangeFeedExecutionOptions> ChangeFeedExecution()
    {
        var settings = new ChangeFeedExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ClaimsExecutionOptions> RuntimeClaimsExecution(ClaimsExecutionOptions? configured = null)
    {
        var value = configured ?? new ClaimsExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<TimeSeriesExecutionOptions> TimeSeriesExecution()
    {
        var settings = new TimeSeriesExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<QueryExecutionOptions> QueryExecution()
    {
        var settings = new QueryExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ZoneTreePointCacheExecutionOptions> PointCacheExecution(ZoneTreePointCacheExecutionOptions? configured = null)
    {
        var settings = configured ?? new ZoneTreePointCacheExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ZoneTreeStorageExecutionOptions> StorageExecution(ZoneTreeStorageExecutionOptions? configured = null)
    {
        var settings = configured ?? new ZoneTreeStorageExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<DatabaseLimits> DatabaseLimits(DatabaseLimits? configured = null)
    {
        var settings = configured ?? new DatabaseLimits();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<DueWorkExecutionOptions> DueWork()
    {
        var settings = new DueWorkExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<EventSourceExecutionOptions> EventSource()
    {
        var settings = new EventSourceExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }
}
