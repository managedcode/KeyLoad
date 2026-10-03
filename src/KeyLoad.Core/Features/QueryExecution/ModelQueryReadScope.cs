using KeyLoad.Storage;
using KeyLoad.Query;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Runs a read-only event or queue query in one authorized, budgeted storage cut.</summary>
    public T WithModelQueryView<T>(string principalId, PartitionRef partition, string resourceName, ModelQuerySource source,
        ReadExecutionBudget budget, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(read);
        ValidatePartition(partition);
        if (!Enum.IsDefined(source.Kind))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "The model query source is unsupported.");
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
                throw Errors.Fail(ErrorCode.Validation, "A queue model source must use its configured resource name.");
            }
            JsonData.Identifier(source.Item);
            if (source.Kind == ModelQuerySourceKind.Events && source.Generation < 1)
            {
                throw Errors.Fail(ErrorCode.Validation, "The event source generation must be positive.");
            }
            Authorization.Require(principal, partition, resourceName, Capability.Query | capability);
            var resource = Resource(budgetedView, partition, resourceName, kind);
            if (source.Kind == ModelQuerySourceKind.QueueMessages && source.Generation != 1)
            {
                throw Errors.Fail(ErrorCode.Validation, "A queue model source generation must be one.");
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
