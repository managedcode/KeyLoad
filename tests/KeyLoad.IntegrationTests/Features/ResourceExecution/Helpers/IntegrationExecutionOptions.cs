using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

/// <summary>Explicit native options composition for standalone database and query regression fixtures.</summary>
internal static class IntegrationExecutionOptions
{
    internal static IOptions<ReplicaConfiguration> ReplicaConfiguration(ReplicaConfiguration value)
    {
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<ReplicaExecutionOptions> ReplicaExecution(ReplicaExecutionOptions? value = null)
    {
        var configured = value ?? new ReplicaExecutionOptions();
        configured.Validate();
        return Options.Create(configured);
    }

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

    internal static IOptions<GraphExecutionOptions> GraphExecution(GraphExecutionOptions? configured = null)
    {
        var value = configured ?? new GraphExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<KeyLoad.Core.ChangeFeedExecutionOptions> ChangeFeedExecution(KeyLoad.Core.ChangeFeedExecutionOptions? configured = null)
    {
        var value = configured ?? new KeyLoad.Core.ChangeFeedExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<KeyLoad.Core.BlobExecutionOptions> BlobExecution(KeyLoad.Core.BlobExecutionOptions? configured = null)
    {
        var value = configured ?? new KeyLoad.Core.BlobExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<NativeClaimsExecutionOptions> NativeClaimsExecution(NativeClaimsExecutionOptions? configured = null)
    {
        var value = configured ?? new NativeClaimsExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<KeyLoad.Core.TimeSeriesExecutionOptions> TimeSeriesExecution(KeyLoad.Core.TimeSeriesExecutionOptions? configured = null)
    {
        var value = configured ?? new KeyLoad.Core.TimeSeriesExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<PartitionMovementCheckpointOptions> MovementCheckpoints(PartitionMovementCheckpointOptions? configured = null)
    {
        var value = configured ?? new PartitionMovementCheckpointOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<KeyLoad.Query.Features.Search.PackedAnnOptions> PackedAnn(KeyLoad.Query.Features.Search.PackedAnnOptions? configured = null)
    {
        var value = configured ?? new KeyLoad.Query.Features.Search.PackedAnnOptions();
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
