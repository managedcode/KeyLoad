using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentReadExecution operations against the caller's database context.</summary>
public static class DocumentReadExecution
{
    internal static DocumentResult? ReadDocumentAtCut(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EntityRef reference, CommitToken? minimumToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        cancellationToken.ThrowIfCancellationRequested();
        engine.Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        if (minimumToken is not null)
        {
            engine.ValidateDocumentSessionToken(view, reference.Partition, minimumToken);
        }

        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResourcePair(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        reference = EntityReferenceLifetime.Bind(reference, resource.Lifetime);
        var record = view.GetRecord<DocumentRecord>(DocumentStorageExecutionDocuments.DocumentKey(reference.Partition, reference.Collection, reference.Id));
        if (record is not null)
        {
            EntityReferenceLifetime.RequireStored(record.Reference, resource.Lifetime);
        }

        var result = record is null || record.Deleted || !engine.Authorization.CanReadRow(principal, record.Access) ? null : engine.Project(principal, resource.Definition, record);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
