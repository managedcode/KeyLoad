using KeyLoad.Core;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares validated database, due-work, event and query policies with execution owners.</summary>
internal sealed record CoreRuntimeOptions(
    IOptions<DatabaseLimits> DatabaseLimits,
    IOptions<DueWorkExecutionOptions> DueWork,
    IOptions<EventSourceExecutionOptions> EventSource,
    IOptions<QueryExecutionOptions> QueryExecution)
{
    internal void RegisterBorrowed(IServiceCollection services)
    {
        services.AddSingleton(DatabaseLimits);
        services.AddSingleton(DueWork);
        services.AddSingleton(EventSource);
        services.AddSingleton(QueryExecution);
    }
}
