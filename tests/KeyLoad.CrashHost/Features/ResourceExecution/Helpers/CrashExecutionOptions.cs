using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>Explicit validated native options composition for genuine test-owned engine and replica fixtures.</summary>
[ConfigurationBinding]
internal static class CrashExecutionOptions
{
    internal static IOptions<NativeTextExecutionOptions> NativeText()
    {
        var settings = new NativeTextExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<MessagingExecutionOptions> Messaging()
    {
        var settings = new MessagingExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<GraphExecutionOptions> GraphExecution()
    {
        var settings = new GraphExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<ChangeFeedExecutionOptions> ChangeFeedExecution()
    {
        var settings = new ChangeFeedExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<TimeSeriesExecutionOptions> TimeSeriesExecution()
    {
        var settings = new TimeSeriesExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<QueryExecutionOptions> QueryExecution()
    {
        var settings = new QueryExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<ZoneTreePointCacheExecutionOptions> PointCacheExecution(ZoneTreePointCacheExecutionOptions? configured = null)
    {
        var settings = configured ?? new ZoneTreePointCacheExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<ZoneTreeStorageExecutionOptions> StorageExecution(ZoneTreeStorageExecutionOptions? configured = null)
    {
        var settings = configured ?? new ZoneTreeStorageExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<DatabaseLimits> DatabaseLimits(DatabaseLimits? configured = null)
    {
        var settings = configured ?? new DatabaseLimits();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<DueWorkExecutionOptions> DueWork()
    {
        var settings = new DueWorkExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<EventSourceExecutionOptions> EventSource()
    {
        var settings = new EventSourceExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<CacheMemoryLimits> CacheMemory(CacheMemoryLimits? configured = null)
    {
        var settings = configured ?? new CacheMemoryLimits();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<ReplicaExecutionOptions> Replica(ReplicaExecutionOptions? configured = null)
    {
        var settings = configured ?? new ReplicaExecutionOptions();
        settings.Validate();
        return Compose(settings, static value => value.Validate());
    }

    internal static IOptions<ReplicaConfiguration> Configuration(ReplicaConfiguration configured)
    {
        configured.Validate();
        return Compose(configured, static value => value.Validate());
    }
    internal static IOptions<DatabaseLimits> DatabaseLimits(bool boundOutbox)
    {
        const int BoundOutboxRecords = 1;
        return DatabaseLimits(boundOutbox ? new DatabaseLimits { MaxOutboxRecords = BoundOutboxRecords } : null);
    }

    internal static IOptions<ReplicaConfiguration> Configuration(string voter, string[] voters,
        string directory, Guid incarnation)
        => Configuration(new ReplicaConfiguration(voter, [.. voters], directory, incarnation));

    internal static IOptions<NodeOptions> Node(NodeOptions selected)
        => Compose(selected, static value => value.Validate());

    internal static IOptions<CrashHostExecutionOptions> Child()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var factory = new OptionsFactory<CrashHostExecutionOptions>(
            [new ConfigureFromConfigurationOptions<CrashHostExecutionOptions>(
                configuration.GetSection(CrashHostExecutionOptions.SectionName))], [],
            [new CrashHostExecutionOptionsValidator()]);
        var options = new OptionsManager<CrashHostExecutionOptions>(factory);
        _ = options.Value;
        return options;
    }

    private static OptionsManager<T> Compose<T>(T selected, Action<T> validate) where T : class
    {
        var options = new OptionsManager<T>(new SelectedFixtureFactory<T>(selected, validate));
        _ = options.Value;
        return options;
    }

    private sealed class SelectedFixtureFactory<T>(T selected, Action<T> validate)
        : OptionsFactory<T>([], []) where T : class
    {
        protected override T CreateInstance(string name)
        {
            validate(selected);
            return selected;
        }
    }
}
