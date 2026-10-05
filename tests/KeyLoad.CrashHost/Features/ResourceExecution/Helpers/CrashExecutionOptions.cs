using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Core;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>Explicit validated native options composition for genuine test-owned engine and replica fixtures.</summary>
internal static class CrashExecutionOptions
{
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

    internal static IOptions<CacheMemoryLimits> CacheMemory(CacheMemoryLimits? configured = null)
    {
        var settings = configured ?? new CacheMemoryLimits();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ReplicaExecutionOptions> Replica(ReplicaExecutionOptions? configured = null)
    {
        var settings = configured ?? new ReplicaExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ReplicaConfiguration> Configuration(ReplicaConfiguration configured)
    {
        configured.Validate();
        return Options.Create(configured);
    }
}
