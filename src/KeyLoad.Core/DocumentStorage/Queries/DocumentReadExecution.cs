using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentReadExecution operations against the caller's database context.</summary>
public static class DocumentReadExecution
{
    internal static DocumentResult? ReadDocumentAtCut(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EntityRef reference, CommitToken? minimumToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        engine.Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        if (minimumToken is not null)
        {
            engine.ValidateDocumentSessionToken(view, reference.Partition, minimumToken);
        }

        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        reference = EntityReferenceLifetime.Bind(view, reference, resource);
        var record = view.GetRecord<DocumentRecord>(DocumentStorageExecutionDocuments.DocumentKey(reference.Partition, reference.Collection, reference.Id));
        if (record is not null)
        {
            EntityReferenceLifetime.RequireStored(view, record.Reference, resource);
        }

        var result = record is null || record.Deleted || !engine.Authorization.CanReadRow(principal, record.Access) ? null : engine.Project(principal, resource, record);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
