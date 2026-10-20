using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MutationAuthorization operations against the caller's database context.</summary>
public static class MutationAuthorization
{
    internal const string MissingMutationSequenceMessage = "A mutation sequence is missing.";
    internal const string MissingMutationEntryMessage = "A mutation entry is missing.";
    // An inbox retry checks current permissions without re-evaluating the old CAS or applying effects again.
    internal static void ReauthorizeEffects(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, ImmutableArray<Mutation> effects)
    {
        if (effects.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, MissingMutationSequenceMessage);
        }

        foreach (var effect in effects)
        {
            if (effect is null)
            {
                throw Errors.Fail(ErrorCode.Validation, MissingMutationEntryMessage);
            }

            engine.ReauthorizeEffect(view, principal, partition, effect);
        }
    }

    internal static void ReauthorizeEffect(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation effect)
    {
        if (effect is UpsertAnalyticsEvents or DeleteAnalyticsEvents)
        {
            engine.AuthorizeAnalyticsMutation(view, principal, partition, effect);
            return;
        }

        if (effect is AdvanceQueueDeadline deadline)
        {
            engine.AuthorizeQueueDeadline(view, principal, partition, deadline);
            return;
        }

        if (effect is RedriveQueueMessage or CancelQueueMessage or ParkPendingQueueMessage)
        {
            engine.AuthorizeQueueLifecycle(view, principal, partition, effect);
            return;
        }

        if (effect is ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
        {
            engine.ReauthorizeRecurringSagaEffect(view, principal, partition, effect);
            return;
        }

        if (effect is ApplyVectorProjection projection)
        {
            engine.ReauthorizeVectorProjection(view, principal, partition, projection);
            return;
        }

        if (effect is CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer or AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair)
        {
            engine.ReauthorizeQueueTransfer(view, principal, partition, effect);
            return;
        }

        if (effect is ApplyCrossPartitionReverseEdge or CompleteCrossPartitionReverseEdge)
        {
            engine.ReauthorizeGraphDelivery(view, principal, partition, effect);
            return;
        }

        engine.AuthorizeComposition(view, principal, partition, effect);
        var resource = engine.ReauthorizeEffectResource(view, partition, effect);
        engine.ReauthorizeEffectDocument(view, principal, partition, effect, resource);
        engine.ReauthorizeEffectFields(principal, resource, effect);
        engine.ReauthorizeEffectHeaders(principal, resource, effect);
        engine.ReauthorizeEffectIndexes(principal, resource, effect);
        engine.ReauthorizeEffectVertices(view, principal, partition, effect);
    }

    internal static void ReauthorizeEffectDocument(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation effect, ResourceDefinition resource)
    {
        var documentId = effect switch
        {
            InsertTypedRow insert => Features.RelationalStorage.RelationalRowValidation.InsertIdentity(resource, insert.Json, engine.OperationLimitsOptions),
            PutDocument put => put.Id,
            PatchDocument patch => patch.Id,
            DeleteDocument delete => delete.Id,
            PutVector vector => vector.Id,
            _ => null
        };
        var document = documentId is null ? null : view.GetRecord<DocumentRecord>(DocumentStorageExecutionDocuments.DocumentKey(partition, effect.Resource, documentId));
        if (document is not null)
        {
            engine.Authorization.RequireWriteRow(principal, document.Access);
        }

        if (effect is PutDocument replacement)
        {
            engine.Authorization.RequireWriteRow(principal, replacement.Access ?? document?.Access ?? new RowAccess());
            if (document is { Deleted: false })
            {
                engine.Authorization.RequireReplacement(principal, resource, replacement.ExplicitReplacement);
            }
        }

        if (effect is InsertTypedRow)
        {
            engine.Authorization.RequireWriteRow(principal, document?.Access ?? new RowAccess());
        }
    }

    internal static ResourceDefinition ReauthorizeEffectResource(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, Mutation effect) => effect is InsertTypedRow ? engine.BatchResource(view, partition, effect.Resource) : engine.Resource(view, partition, effect.Resource);
    internal static void ReauthorizeEffectFields(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, Mutation effect)
    {
        if (effect is PatchDocument patchFields)
        {
            foreach (var field in patchFields.Patches)
            {
                engine.Authorization.RequireFieldWrite(principal, resource, field.Path);
            }

            return;
        }

        if (effect is PutVector vectorField)
        {
            engine.Authorization.RequireFieldUse(principal, resource, vectorField.Field);
            engine.Authorization.RequireFieldWrite(principal, resource, vectorField.Field);
            return;
        }

        if (effect is InsertTypedRow or PutDocument or AppendEvents or PublishTopic or EnqueueMessage or UpsertEdge or AppendSamples)
        {
            foreach (var policy in resource.FieldPolicies)
            {
                engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
            }
        }
    }

    internal static void ReauthorizeEffectHeaders(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, Mutation effect)
    {
        if (effect is AppendEvents or PublishTopic or EnqueueMessage)
        {
            foreach (var policy in resource.HeaderPolicies)
            {
                engine.Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
            }
        }
    }

    internal static void ReauthorizeEffectIndexes(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, Mutation effect)
    {
        if (effect is InsertTypedRow or PutDocument or PatchDocument or DeleteDocument)
        {
            foreach (var index in resource.Indexes)
            {
                foreach (var field in index.Fields)
                {
                    engine.Authorization.RequireFieldUse(principal, resource, field);
                }
            }
        }
    }

    internal static void ReauthorizeEffectVertices(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation effect)
    {
        if (effect is UpsertEdge upsert)
        {
            engine.VisibleVertex(view, principal, upsert.From);
            engine.VisibleVertex(view, principal, upsert.To);
        }

        if (effect is DeleteEdge remove && view.GetRecord<EdgeRecord>(GraphTraversalStorage.EdgeKey(partition, remove.Graph, remove.EdgeId)) is { } edge)
        {
            engine.VisibleVertex(view, principal, edge.From);
            engine.VisibleVertex(view, principal, edge.To);
        }
    }
}
