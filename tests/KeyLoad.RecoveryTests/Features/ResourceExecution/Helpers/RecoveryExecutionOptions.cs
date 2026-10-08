using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests;

/// <summary>Explicit validated native options composition for genuine test-owned engine and replica fixtures.</summary>
internal static class RecoveryExecutionOptions
{
    internal static IOptions<NativeProcessReadinessOptions> NativeProcessReadiness()
    {
        var settings = new NativeProcessReadinessOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<NativeTextExecutionOptions> NativeText()
    {
        var settings = new NativeTextExecutionOptions();
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

    internal static IOptions<BlobExecutionOptions> BlobExecution()
    {
        var settings = new BlobExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<NativeClaimsExecutionOptions> NativeClaimsExecution(NativeClaimsExecutionOptions? configured = null)
    {
        var value = configured ?? new NativeClaimsExecutionOptions();
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

    internal static IOptions<ReplicaExecutionOptions> Replica(ReplicaExecutionOptions? configured = null)
    {
        var settings = configured ?? new ReplicaExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<PeerDiscoveryOptions> PeerDiscovery(PeerDiscoveryOptions? configured = null)
    {
        var settings = configured ?? new PeerDiscoveryOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ReplicaConfiguration> Configuration(ReplicaConfiguration configured)
    {
        configured.Validate();
        return Options.Create(configured);
    }
}
