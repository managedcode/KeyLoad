using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal FollowerDocumentSnapshot CaptureFollowerDocument(string principalId, ReadFollowerDocumentRequestV1 request,
        PhysicalShardRecord owner, string replicaId, long term, CancellationToken cancellationToken)
    {
        ValidateFollowerReadRequest(request);
        return Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            Authorization.Require(principal, request.Reference.Partition, request.Reference.Collection, Capability.DocumentsRead);
            _ = Resource(view, request.Reference.Partition, request.Reference.Collection, ResourceKind.Collection);
            var token = FollowerReadToken(view, request.Reference.Partition, owner);
            var record = view.GetRecord<DocumentRecord>(DocumentKey(request.Reference.Partition,
                request.Reference.Collection, request.Reference.Id));
            if (record is not null && Encoding.UTF8.GetByteCount(record.Json) > Limits.MaxDocumentBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, FollowerReadInvalid); }
            var identity = Store.Identity;
            cancellationToken.ThrowIfCancellationRequested();
            return new FollowerDocumentSnapshot(record, token, identity.NodeId, identity.ReadGeneration,
                owner, replicaId, term);
        });
    }

    internal FollowerDocumentReadResultV1 CompleteFollowerDocument(string principalId, FollowerDocumentReadCapability capability,
        FollowerDocumentSnapshot snapshot, string replicaId, long authorityTerm, CancellationToken cancellationToken)
        => Store.Read<FollowerDocumentReadResultV1>(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = capability.Request;
            ValidateFollowerReadRequest(request);
            var principal = RevalidateCredential(view, capability.Credential, principalId, Clock.GetUtcNow());
            Authorization.Require(principal, request.Reference.Partition, request.Reference.Collection, Capability.DocumentsRead);
            var resource = Resource(view, request.Reference.Partition, request.Reference.Collection, ResourceKind.Collection);
            var authority = FollowerReadToken(view, request.Reference.Partition, snapshot.Owner);
            if (request.MinimumToken is { } minimum)
            { ValidateDocumentSessionToken(view, request.Reference.Partition, minimum); }
            var identity = Store.Identity;
            var lag = ValidateFollowerDataCut(request, snapshot, authority, identity.NodeId, identity.ReadGeneration);
            if (replicaId != snapshot.ReplicaId || authorityTerm < snapshot.Term)
            { throw Errors.Fail(ErrorCode.OwnershipLost, FollowerReadOwnerChanged); }
            var current = ReadVisibleDocument(view, principal, request.Reference);
            var old = snapshot.Record;
            var document = current is null || old is null || old.Deleted || !Authorization.CanReadRow(principal, old.Access)
                ? null : Project(principal, resource, old);
            cancellationToken.ThrowIfCancellationRequested();
            return new(FollowerReadVersion, DocumentFollowerReadMode.FollowerCommittedSnapshot, replicaId,
                snapshot.Term, authorityTerm, snapshot.Token, authority, lag, principal.PolicyEpoch, document);
        });
}
