using System.Text;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal FollowerDocumentSnapshot CaptureFollowerDocument(string principalId, ReadFollowerDocumentRequestV1 request,
        PhysicalShardRecord owner, string replicaId, long term, CancellationToken cancellationToken)
    {
        ValidateFollowerReadRequest(request);
        return Store.Read(view => CaptureFollowerDocumentAtCut(view, principalId, request, owner,
            replicaId, term, cancellationToken).Snapshot);
    }

    private (FollowerDocumentSnapshot Snapshot, ResourceDefinition Resource) CaptureFollowerDocumentAtCut(
        IKeyValueView view, string principalId, ReadFollowerDocumentRequestV1 request,
        PhysicalShardRecord owner, string replicaId, long term, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, request.Reference.Partition, request.Reference.Collection, Capability.DocumentsRead);
        var resource = BindAuthorizedReadResource(view, request.Reference.Partition, request.Reference.Collection, ResourceKind.Collection);
        _ = EntityReferenceLifetime.Bind(view, request.Reference, resource);
        var token = FollowerReadToken(view, request.Reference.Partition, owner);
        var record = view.GetRecord<DocumentRecord>(DocumentKey(request.Reference.Partition,
            request.Reference.Collection, request.Reference.Id));
        if (record is not null)
        {
            EntityReferenceLifetime.RequireStored(view, record.Reference, resource);
        }
        if (record is not null && Encoding.UTF8.GetByteCount(record.Json) > Limits.MaxDocumentBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, FollowerReadInvalid);
        }
        var identity = Store.Identity;
        cancellationToken.ThrowIfCancellationRequested();
        return (new FollowerDocumentSnapshot(record, token, identity.NodeId, identity.ReadGeneration,
            owner, replicaId, term), resource);
    }

    internal FollowerDocumentReadResultV1 CompleteFollowerDocument(string principalId, FollowerDocumentReadCapability capability,
        FollowerDocumentSnapshot snapshot, string replicaId, long authorityTerm, CancellationToken cancellationToken)
        => Store.Read(view => CompleteFollowerDocumentAtCut(view, principalId, capability, snapshot,
            replicaId, authorityTerm, cancellationToken));

    private FollowerDocumentReadResultV1 CompleteFollowerDocumentAtCut(IKeyValueView view, string principalId,
        FollowerDocumentReadCapability capability, FollowerDocumentSnapshot snapshot, string replicaId,
        long authorityTerm, CancellationToken cancellationToken, Action<ResourceDefinition>? requireLifetime = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = capability.Request;
        ValidateFollowerReadRequest(request);
        var principal = RevalidateCredential(view, capability.Credential, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, request.Reference.Partition, request.Reference.Collection, Capability.DocumentsRead);
        var resource = BindAuthorizedReadResource(view, request.Reference.Partition, request.Reference.Collection, ResourceKind.Collection);
        requireLifetime?.Invoke(resource);
        var authority = FollowerReadToken(view, request.Reference.Partition, snapshot.Owner);
        if (request.MinimumToken is { } minimum)
        {
            ValidateDocumentSessionToken(view, request.Reference.Partition, minimum);
        }
        var identity = Store.Identity;
        var lag = ValidateFollowerDataCut(request, snapshot, authority, identity.NodeId, identity.ReadGeneration);
        if (replicaId != snapshot.ReplicaId || authorityTerm < snapshot.Term)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, FollowerReadOwnerChanged);
        }
        var current = ReadVisibleDocument(view, principal, request.Reference);
        var old = snapshot.Record;
        if (old is not null)
        {
            EntityReferenceLifetime.RequireStored(view, old.Reference, resource);
        }
        var document = current is null || old is null || old.Deleted || !Authorization.CanReadRow(principal, old.Access)
            ? null : Project(principal, resource, old);
        cancellationToken.ThrowIfCancellationRequested();
        return new(FollowerReadVersion, DocumentFollowerReadMode.FollowerCommittedSnapshot, replicaId,
            snapshot.Term, authorityTerm, snapshot.Token, authority, lag, principal.PolicyEpoch, document);
    }
}
