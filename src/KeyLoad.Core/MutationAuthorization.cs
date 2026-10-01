using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    // An inbox retry checks current permissions without re-evaluating the old CAS or applying effects again.
    private void ReauthorizeEffects(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation[] effects)
    {
        foreach (var effect in effects)
        {
            var resource = Resource(view, partition, effect.Resource);
            string? documentId = effect switch
            {
                PutDocument put => put.Id, PatchDocument patch => patch.Id,
                DeleteDocument delete => delete.Id, PutVector vector => vector.Id, _ => null
            };
            var document = documentId is null ? null : view.GetRecord<DocumentRecord>(DocumentKey(partition, effect.Resource, documentId));
            if (document is not null) Authorization.RequireWriteRow(principal, document.Access);
            if (effect is PutDocument replacement)
            {
                Authorization.RequireWriteRow(principal, replacement.Access ?? document?.Access ?? new RowAccess());
                if (document is { Deleted: false }) Authorization.RequireReplacement(principal, resource, replacement.ExplicitReplacement);
            }
            if (effect is PatchDocument patchFields)
                foreach (var field in patchFields.Patches) Authorization.RequireFieldWrite(principal, resource, field.Path);
            else if (effect is PutVector vectorField)
            {
                Authorization.RequireFieldUse(principal, resource, vectorField.Field);
                Authorization.RequireFieldWrite(principal, resource, vectorField.Field);
            }
            else if (effect is PutDocument or AppendEvents or PublishTopic or EnqueueMessage or UpsertEdge or AppendSamples)
                foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
            if (effect is AppendEvents or PublishTopic or EnqueueMessage)
                foreach (var policy in resource.HeaderPolicies)
                    Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
            if (effect is PutDocument or PatchDocument or DeleteDocument)
                foreach (var index in resource.Indexes)
                    foreach (var field in index.Fields) Authorization.RequireFieldUse(principal, resource, field);
            if (effect is UpsertEdge upsert)
            { VisibleVertex(view, principal, upsert.From); VisibleVertex(view, principal, upsert.To); }
            if (effect is DeleteEdge remove && view.GetRecord<EdgeRecord>(EdgeKey(partition, remove.Graph, remove.EdgeId)) is { } edge)
            { VisibleVertex(view, principal, edge.From); VisibleVertex(view, principal, edge.To); }
        }
    }
}
