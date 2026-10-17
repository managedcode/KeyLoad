using System.Collections.Immutable;
using KeyLoad.Core.Features.RelationalStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public partial class DatabaseEngine
{
    private const string TypedJoinRequiresRelationalCollections = "The endpoint join requires two distinct typed relational collections.";
    private const string TypedJoinReferenceMismatch = "The queue link does not match its declared endpoint collections.";
    private const string TypedJoinRecordMismatch = "The typed endpoint record has a different canonical identity.";

    private (string FromPrimaryKey, string ToPrimaryKey)? AuthorizeTypedEndpointJoin(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, QueueToGraph command, BatchResourceAdmission admission)
    {
        if (command.FromCollection is null && command.ToCollection is null)
        {
            return null;
        }
        if (command.FromCollection is null || command.ToCollection is null
            || StringComparer.Ordinal.Equals(command.FromCollection, command.ToCollection))
        {
            throw Errors.Fail(ErrorCode.Validation, TypedJoinRequiresRelationalCollections);
        }
        var fromPrimaryKey = AuthorizeTypedEndpointResource(view, principal, partition, command.FromCollection, admission);
        var toPrimaryKey = AuthorizeTypedEndpointResource(view, principal, partition, command.ToCollection, admission);
        return (fromPrimaryKey, toPrimaryKey);
    }

    private string AuthorizeTypedEndpointResource(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, BatchResourceAdmission admission)
    {
        JsonData.Identifier(collection);
        Authorization.Require(principal, partition, collection, Capability.DocumentsRead);
        var resource = BatchResource(view, partition, collection, admission, ResourceKind.Collection);
        if (resource.RelationalSchema is not { } schema)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, TypedJoinRequiresRelationalCollections);
        }
        RelationalRowValidation.ValidateSchema(resource);
        Authorization.RequireFieldUse(principal, resource, JsonData.Path([schema.PrimaryKey]));
        return schema.PrimaryKey;
    }

    private QueueGraphLink? SelectTypedEndpointPair(IKeyValueView view, PrincipalRecord principal,
        QueueToGraph command, QueueGraphLink link, ReadExecutionBudget budget,
        string fromPrimaryKey, string toPrimaryKey)
    {
        if (!StringComparer.Ordinal.Equals(link.From.Collection, command.FromCollection)
            || !StringComparer.Ordinal.Equals(link.To.Collection, command.ToCollection))
        {
            throw Errors.Fail(ErrorCode.Validation, TypedJoinReferenceMismatch);
        }
        // Both genuine probes are charged, including absent and row-denied endpoints.
        var from = ReadTypedEndpoint(view, principal, link.From, budget, fromPrimaryKey);
        var to = ReadTypedEndpoint(view, principal, link.To, budget, toPrimaryKey);
        return from is null || to is null ? null : link with
        {
            From = from.Reference,
            To = to.Reference
        };
    }

    private DocumentRecord? ReadTypedEndpoint(IKeyValueView view, PrincipalRecord principal,
        EntityRef reference, ReadExecutionBudget budget, string primaryKey)
    {
        var record = budget.ReadRecord<DocumentRecord>(view,
            DocumentKey(reference.Partition, reference.Collection, reference.Id));
        if (record is null)
        {
            return null;
        }
        if (record.Deleted || !Authorization.CanReadRow(principal, record.Access))
        {
            return null;
        }
        // Resource and row authority were checked before resolving the caller's explicit lookup.
        _ = BindCompositionInputRecord(budget.CreateView(view), reference, record);
        RequireTypedEndpointPrimaryIdentity(record, primaryKey);
        return record;
    }

    private List<Mutation> ExpandQueueToGraph(IAtomicTransaction transaction, PrincipalRecord principal,
        PartitionRef partition, QueueToGraph command, DateTimeOffset now, ReadExecutionBudget budget)
    {
        if (command.FromCollection is null && command.ToCollection is null)
        {
            return ExpandQueueToGraphSelection(transaction, principal, partition, command, now, budget, transaction);
        }
        var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
        try
        {
            using var lease = budget.EnterReadGrant(grant);
            return ExpandQueueToGraphSelection(transaction, principal, partition, command, now, budget,
                budget.CreateView(transaction));
        }
        finally
        {
            // The using lease exits before settled completion, including failure paths.
            budget.CompleteSettledReadGrant(grant);
        }
    }

    private void ValidateTypedEndpointOutcomeReferences(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ImmutableArray<EntityRef> references, ReadExecutionBudget budget)
    {
        var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
        try
        {
            using var lease = budget.EnterReadGrant(grant);
            var chargedView = budget.CreateView(view);
            foreach (var reference in references)
            {
                ValidateCompositionReference(reference, partition);
                EntityReferenceLifetime.RequireBound(reference);
                // A saved typed authority is never rebound from its collection name.
                // The budgeted view charges the resource and document once; no second source scan.
                var document = VisibleVertex(chargedView, principal, reference);
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
