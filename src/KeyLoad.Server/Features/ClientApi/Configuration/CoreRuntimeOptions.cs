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
        _ = QueryExecution.Value;
        _ = CacheMemory.Value;
        _ = CacheReadPermit.Value;
        _ = RuntimeJournal.Value;
    }

    internal void RegisterBorrowed(IServiceCollection services)
    {
        services.AddSingleton(DatabaseLimits);
        services.AddSingleton(DueWork);
        services.AddSingleton(EventSource);
        services.AddSingleton(Messaging);
        services.AddSingleton(GraphExecution);
        services.AddSingleton(QueryExecution);
        services.AddSingleton(CacheMemory);
        services.AddSingleton(CacheReadPermit);
        services.AddSingleton(RuntimeJournal);
    }
}
