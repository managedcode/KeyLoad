using KeyLoad.Core;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares validated database, due-work, event and query policies with execution owners.</summary>
internal sealed record CoreRuntimeOptions(
    IOptions<DatabaseLimits> DatabaseLimits,
    IOptions<DueWorkExecutionOptions> DueWork,
    IOptions<EventSourceExecutionOptions> EventSource,
    IOptions<MessagingExecutionOptions> Messaging,
    IOptions<GraphExecutionOptions> GraphExecution,
    IOptions<ChangeFeedExecutionOptions> ChangeFeedExecution,
    IOptions<ClaimsExecutionOptions> RuntimeClaimsExecution,
    IOptions<TimeSeriesExecutionOptions> TimeSeriesExecution,
    IOptions<KeyLoad.Query.Search.PackedAnnOptions> PackedAnn,
    IOptions<KeyLoad.Core.Search.AnnSeedOptions> AnnSeed,
    IOptions<KeyLoad.Query.Search.PackedAnnStorageOptions> PackedAnnStorage,
    IOptions<QueryExecutionOptions> QueryExecution,
    IOptions<CacheMemoryLimits> CacheMemory,
    IOptions<CacheReadPermitOptions> CacheReadPermit,
    IOptions<RuntimeJournalOptions> RuntimeJournal)
{
    internal void ValidateBeforePhysicalOwnership()
    {
        _ = DatabaseLimits.Value;
        _ = DueWork.Value;
        _ = EventSource.Value;
        _ = Messaging.Value;
        _ = GraphExecution.Value;
        _ = ChangeFeedExecution.Value;
        _ = RuntimeClaimsExecution.Value;
        _ = TimeSeriesExecution.Value;
        _ = PackedAnn.Value;
        _ = AnnSeed.Value;
        _ = PackedAnnStorage.Value;
        _ = QueryExecution.Value;
        _ = CacheMemory.Value;
        _ = CacheReadPermit.Value;
        _ = RuntimeJournal.Value;
    }

}
