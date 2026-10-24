using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueryExecutionQueries operations against the caller's database context.</summary>
public static class QueryExecutionQueries
{
    internal const string InvalidEventSourceGenerationDetail = "The event source generation must be positive.";
    internal const string InvalidQueueSourceGenerationDetail = "A queue model source generation must be one.";
    internal const long QueueModelGeneration = 1;
    /// <summary>Runs a read-only event or queue query in one authorized, budgeted storage cut.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "principalId">The principal Id.</param>
    /// <param name = "partition">The partition.</param>
    /// <param name = "resourceName">The resource Name.</param>
    /// <param name = "source">The source.</param>
    /// <param name = "budget">The budget.</param>
    /// <param name = "read">The read.</param>
    public static T WithModelQueryView<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string resourceName, ModelQuerySource source, ReadExecutionBudget budget, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read)
    {
        ArgumentNullException.ThrowIfNull(engine);
        const string WithModelQueryViewDetailText = "The model query source is unsupported.";
        const string QueueResourceNameMismatchDetail = "A queue model source must use its configured resource name.";
        const string TopicResourceNameMismatchDetail = "A topic model source must use its configured resource name.";
        const int FirstSourceGeneration = 1;
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(read);
        EngineResourceAccess.ValidatePartition(partition);
        if (!Enum.IsDefined(source.Kind))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, WithModelQueryViewDetailText);
        }

        return engine.Store.Read(rawView =>
        {
            budget.Check();
            var budgetedView = budget.CreateView(rawView);
            var principal = engine.Principal(budgetedView, principalId, engine.Clock.GetUtcNow());
            var kind = source.Kind switch
            {
                ModelQuerySourceKind.Events => ResourceKind.StreamSet,
                ModelQuerySourceKind.TopicEvents => ResourceKind.Topic,
                _ => ResourceKind.WorkQueue
            };
            var capability = source.Kind switch
            {
                ModelQuerySourceKind.Events => Capability.EventsRead,
                ModelQuerySourceKind.TopicEvents => Capability.TopicsRead,
                _ => Capability.QueueInspect
            };
            if (source.Kind == ModelQuerySourceKind.QueueMessages && source.Item != resourceName)
            {
                throw Errors.Fail(ErrorCode.Validation, QueueResourceNameMismatchDetail);
            }

            if (source.Kind == ModelQuerySourceKind.TopicEvents && source.Item != resourceName)
            {
                throw Errors.Fail(ErrorCode.Validation, TopicResourceNameMismatchDetail);
            }

            JsonData.Identifier(source.Item);
            if (source.Kind != ModelQuerySourceKind.QueueMessages && source.Generation < FirstSourceGeneration)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidEventSourceGenerationDetail);
            }

            engine.Authorization.Require(principal, partition, resourceName, Capability.Query | capability);
            var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(budgetedView, partition, resourceName, kind);
            if (source.Kind == ModelQuerySourceKind.QueueMessages && source.Generation != QueueModelGeneration)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidQueueSourceGenerationDetail);
            }

            budget.Check();
            return read(rawView, principal, resource);
        });
    }

    /// <summary>Visits authorized event or queue rows within the caller's active read cut.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "view">The view.</param>
    /// <param name = "principal">The principal.</param>
    /// <param name = "partition">The partition.</param>
    /// <param name = "resource">The resource.</param>
    /// <param name = "source">The source.</param>
    /// <param name = "budget">The budget.</param>
    /// <param name = "explain">The explain.</param>
    /// <param name = "accept">The accept.</param>
    public static void VisitModelQueryRows(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, ResourceDefinition resource, ModelQuerySource source, ReadExecutionBudget budget, bool explain, Action<DocumentRecord> accept)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        if (source.Kind == ModelQuerySourceKind.TopicEvents)
        {
            engine.VisitTopicModelRows(view, partition, resource, source, budget, explain, accept);
            return;
        }

        QueryExecution.ModelQueryReadRows.Visit(engine, view, principal, partition, resource, source, budget, explain, accept);
    }
}
