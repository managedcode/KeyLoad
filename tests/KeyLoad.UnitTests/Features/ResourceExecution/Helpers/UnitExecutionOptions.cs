using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit native options composition for standalone database and query regression fixtures.</summary>
internal static class UnitExecutionOptions
{
    internal static IOptions<ReplicaConfiguration> ReplicaConfiguration(ReplicaConfiguration value)
        => ReplicaExecutionTestOptions.Configuration(value);

    internal static IOptions<ReplicaExecutionOptions> ReplicaExecution(ReplicaExecutionOptions? value = null)
        => ReplicaExecutionTestOptions.Execution(value);

    internal static IOptions<ZoneTreeStorageExecutionOptions> StorageExecution(ZoneTreeStorageExecutionOptions? configured = null)
    {
        var value = configured ?? new ZoneTreeStorageExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<ZoneTreePointCacheExecutionOptions> PointCacheExecution(ZoneTreePointCacheExecutionOptions? configured = null)
    {
        var value = configured ?? new ZoneTreePointCacheExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<DatabaseLimits> DatabaseLimits(DatabaseLimits? configured = null)
    {
        var value = configured ?? new DatabaseLimits();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<DueWorkExecutionOptions> DueWork(DueWorkExecutionOptions? configured = null)
    {
        var value = configured ?? new DueWorkExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<EventSourceExecutionOptions> EventSource(EventSourceExecutionOptions? configured = null)
    {
        var value = configured ?? new EventSourceExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<MessagingExecutionOptions> Messaging(MessagingExecutionOptions? configured = null)
    {
        var value = configured ?? new MessagingExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<QueryExecutionOptions> QueryExecution(QueryExecutionOptions? configured = null)
    {
        var value = configured ?? new QueryExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
