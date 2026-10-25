using System.Collections.Immutable;
using KeyLoad.Core.RelationalStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCompositionTypedEndpointJoin operations against the caller's database context.</summary>
public static class DatabaseCompositionTypedEndpointJoin
{
    internal const string TypedJoinRequiresRelationalCollections = "The endpoint join requires two distinct typed relational collections.";
    internal const string TypedJoinReferenceMismatch = "The queue link does not match its declared endpoint collections.";
    internal const string TypedJoinRecordMismatch = "The typed endpoint record has a different canonical identity.";
    internal static (bool HasValue, (string FromPrimaryKey, string ToPrimaryKey)? Value) AuthorizeTypedEndpointJoin(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, QueueToGraph command)
    {
        if (command.FromCollection is null && command.ToCollection is null)
        {
            return (false, default((string FromPrimaryKey, string ToPrimaryKey)?));
        }

        if (command.FromCollection is null || command.ToCollection is null || StringComparer.Ordinal.Equals(command.FromCollection, command.ToCollection))
        {
            throw Errors.Fail(ErrorCode.Validation, TypedJoinRequiresRelationalCollections);
        }

        var fromPrimaryKey = engine.AuthorizeTypedEndpointResource(view, principal, partition, command.FromCollection);
        var toPrimaryKey = engine.AuthorizeTypedEndpointResource(view, principal, partition, command.ToCollection);
        return (true, (((string FromPrimaryKey, string ToPrimaryKey)?)((fromPrimaryKey, toPrimaryKey))));
    }

    internal static string AuthorizeTypedEndpointResource(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection)
    {
        JsonData.Identifier(collection);
        engine.Authorization.Require(principal, partition, collection, Capability.DocumentsRead);
        var resource = engine.BatchResource(view, partition, collection, ResourceKind.Collection);
        if (resource.RelationalSchema is not { } schema)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, TypedJoinRequiresRelationalCollections);
        }

        RelationalRowValidation.ValidateSchema(resource);
        engine.Authorization.RequireFieldUse(principal, resource, JsonData.Path([schema.PrimaryKey]));
        return schema.PrimaryKey;
    }

    internal static (bool HasValue, QueueGraphLink? Value) SelectTypedEndpointPair(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, QueueToGraph command, QueueGraphLink link, ReadExecutionBudget budget, string fromPrimaryKey, string toPrimaryKey)
    {
        if (!StringComparer.Ordinal.Equals(link.From.Collection, command.FromCollection) || !StringComparer.Ordinal.Equals(link.To.Collection, command.ToCollection))
        {
            throw Errors.Fail(ErrorCode.Validation, TypedJoinReferenceMismatch);
        }

        // Both genuine probes are charged, including absent and row-denied endpoints.
        var from = (engine.ReadTypedEndpoint(view, principal, link.From, budget, fromPrimaryKey)).Value;
        var to = (engine.ReadTypedEndpoint(view, principal, link.To, budget, toPrimaryKey)).Value;
        return (from is null || to is null ? null : link with
        {
            From = from.Reference,
            To = to.Reference
        }) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(QueueGraphLink?));
    }

    internal static (bool HasValue, DocumentRecord? Value) ReadTypedEndpoint(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EntityRef reference, ReadExecutionBudget budget, string primaryKey)
    {
        var record = budget.ReadRecord<DocumentRecord>(view, DocumentStorageExecutionDocuments.DocumentKey(reference.Partition, reference.Collection, reference.Id));
        if (record is null)
        {
            return (false, default(DocumentRecord?));
        }

        if (record.Deleted || !engine.Authorization.CanReadRow(principal, record.Access))
        {
            return (false, default(DocumentRecord?));
        }

        // Resource and row authority were checked before resolving the caller's explicit lookup.
        _ = engine.BindCompositionInputRecord(budget.CreateView(view), reference, record);
        DatabaseCompositionRetainedPrimaryIdentity.RequireTypedEndpointPrimaryIdentity(record, primaryKey);
        return (true, ((DocumentRecord?)(record)));
    }

    internal static List<Mutation> ExpandQueueToGraph(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, PartitionRef partition, QueueToGraph command, DateTimeOffset now, ReadExecutionBudget budget)
    {
        if (command.FromCollection is null && command.ToCollection is null)
        {
            return engine.ExpandQueueToGraphSelection(transaction, principal, partition, command, now, budget, transaction);
        }

        var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
        try
        {
            using var lease = budget.EnterReadGrant(grant);
            return engine.ExpandQueueToGraphSelection(transaction, principal, partition, command, now, budget, budget.CreateView(transaction));
        }
        finally
        {
            // The using lease exits before settled completion, including failure paths.
            budget.CompleteSettledReadGrant(grant);
        }
    }

    internal static void ValidateTypedEndpointOutcomeReferences(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, ImmutableArray<EntityRef> references, ReadExecutionBudget budget)
    {
        var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
        try
        {
            using var lease = budget.EnterReadGrant(grant);
            var chargedView = budget.CreateView(view);
            foreach (var reference in references)
            {
                DatabaseCompositionIdentity.ValidateCompositionReference(reference, partition);
                EntityReferenceLifetime.RequireBound(reference);
                // A saved typed authority is never rebound from its collection name.
                // The budgeted view charges the resource and document once; no second source scan.
                var document = engine.VisibleVertex(chargedView, principal, reference);
                if (document.Reference != reference)
                {
                    throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch);
                }
            }
        }
        finally
        {
            budget.CompleteSettledReadGrant(grant);
        }
    }
}
