using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit execution options composition for standalone database and query fixtures.</summary>
internal static partial class UnitExecutionOptions
{

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

    internal static IOptions<ClaimsExecutionOptions> RuntimeClaimsExecution(ClaimsExecutionOptions? configured = null)
    {
        var value = configured ?? new ClaimsExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<KeyLoad.Core.TimeSeriesExecutionOptions> TimeSeriesExecution(KeyLoad.Core.TimeSeriesExecutionOptions? configured = null)
    {
        var value = configured ?? new KeyLoad.Core.TimeSeriesExecutionOptions();
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
