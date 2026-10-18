using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private DocumentResult? ReadDocumentAtCut(IKeyValueView view, PrincipalRecord principal,
        EntityRef reference, CommitToken? minimumToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        if (minimumToken is not null)
        {
            ValidateDocumentSessionToken(view, reference.Partition, minimumToken);
        }
        var resource = BindAuthorizedReadResource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        reference = EntityReferenceLifetime.Bind(view, reference, resource);
        var record = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        if (record is not null)
        {
            EntityReferenceLifetime.RequireStored(view, record.Reference, resource);
        }
        var result = record is null || record.Deleted || !Authorization.CanReadRow(principal, record.Access)
            ? null : Project(principal, resource, record);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
