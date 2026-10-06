using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidEventSourceGenerationDetail = "The event source generation must be positive.";
    private const string InvalidQueueSourceGenerationDetail = "A queue model source generation must be one.";
    private const long QueueModelGeneration = 1;

    /// <summary>Runs a read-only event or queue query in one authorized, budgeted storage cut.</summary>
    public T WithModelQueryView<T>(string principalId, PartitionRef partition, string resourceName, ModelQuerySource source,
        ReadExecutionBudget budget, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read)
    {
        const string WithModelQueryViewDetailText = "The model query source is unsupported.";
        const string QueueResourceNameMismatchDetail = "A queue model source must use its configured resource name.";
        const int FirstSourceGeneration = 1;

        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(read);
        ValidatePartition(partition);
        if (!Enum.IsDefined(source.Kind))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, WithModelQueryViewDetailText);
        }

        return Store.Read(rawView =>
        {
            budget.Check();
            var budgetedView = budget.CreateView(rawView);
            var principal = Principal(budgetedView, principalId, Clock.GetUtcNow());
            var kind = source.Kind == ModelQuerySourceKind.Events ? ResourceKind.StreamSet : ResourceKind.WorkQueue;
            var capability = source.Kind == ModelQuerySourceKind.Events ? Capability.EventsRead : Capability.QueueInspect;
            if (source.Kind == ModelQuerySourceKind.QueueMessages && source.Item != resourceName)
            {
                throw Errors.Fail(ErrorCode.Validation, QueueResourceNameMismatchDetail);
            }
            JsonData.Identifier(source.Item);
            if (source.Kind == ModelQuerySourceKind.Events && source.Generation < FirstSourceGeneration)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidEventSourceGenerationDetail);
            }
            Authorization.Require(principal, partition, resourceName, Capability.Query | capability);
            var resource = Resource(budgetedView, partition, resourceName, kind);
            if (source.Kind == ModelQuerySourceKind.QueueMessages && source.Generation != QueueModelGeneration)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidQueueSourceGenerationDetail);
            }

            budget.Check();
            return read(rawView, principal, resource);
        });
    }

    /// <summary>Visits authorized event or queue rows within the caller's active read cut.</summary>
    public void VisitModelQueryRows(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        ResourceDefinition resource, ModelQuerySource source, ReadExecutionBudget budget, bool explain,
        Action<DocumentRecord> accept)
        => Features.QueryExecution.ModelQueryReadRows.Visit(this, view, principal, partition, resource,
            source, budget, explain, accept);
}
