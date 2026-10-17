using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OwnedDocumentReadResultV1 ReadOwnedDocumentV2(string principalId, OwnedDocumentReadRequestV2 request,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant)
        => Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var frame = request.Frame;
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            Authorization.Require(principal, request.Request.Reference.Partition, request.Request.Reference.Collection, Capability.DocumentsRead);
            if (principal.TenantId != frame.Principal.TenantId || principal.Id != frame.Principal.Id)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentFenceChanged);
            }
            if (frame.Version != DocumentLifetimeVersion || frame.QueryId == Guid.Empty
                || frame.ExpiresAt <= EvaluationClock.GetUtcNow()
                || frame.Route.PrincipalId != principal.Id || frame.Route.Tenant != principal.TenantId
                || frame.Route.PolicyEpoch != frame.Principal.PolicyEpoch || frame.Resource.Name != frame.Reference.Collection
                || frame.Resource.Kind != ResourceKind.Collection || frame.Resource.TransactionDomainId != frame.Reference.Partition.TransactionDomainId
                || frame.Principal.PolicyEpoch <= DocumentTokenInitialPosition
                || frame.Principal.Revoked || frame.Principal.ExpiresAt <= EvaluationClock.GetUtcNow())
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged);
            }
            PartitionControlLifetimeCoverage.Require(frame.Coverage, frame.Reference.Partition, [frame.Resource], Limits.MaxBatchBytes);
            var sourceLifetime = frame.Coverage.Lifetimes.Single();
            EntityReferenceLifetime.RequireAdmitted(frame.Reference, request.Request.Reference, sourceLifetime);
            var placement = ReadPlacementWitness(charged, frame.Reference.Partition);
            if (placement.PhysicalShardId != request.Owner.PhysicalShardId || placement.Incarnation != request.Owner.Incarnation
                || placement.PlacementEpoch != request.Owner.PlacementEpoch || !placement.VoterIds.SequenceEqual(request.Owner.VoterIds, StringComparer.Ordinal)
                || frame.Route.Placement.Partition != placement.Partition
                || frame.Route.Placement.PhysicalShardId != placement.PhysicalShardId
                || frame.Route.Placement.Incarnation != placement.Incarnation
                || frame.Route.Destination.Owner.PhysicalShardId != request.Owner.PhysicalShardId)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, DocumentOwnerNodeMismatch);
            }
            var targetResource = BindAuthorizedReadResource(charged, frame.Reference.Partition, frame.Reference.Collection, ResourceKind.Collection);
            Authorization.Require(frame.Principal, frame.Reference.Partition, frame.Reference.Collection, Capability.DocumentsRead);
            work.CaptureAuthenticatedResourceLifetimes(this, charged, frame.Coverage, [frame.Resource]);
            if (request.Request.MinimumToken is not null)
            {
                ValidateDocumentSessionToken(charged, frame.Reference.Partition, request.Request.MinimumToken);
            }
            var record = charged.GetRecord<DocumentRecord>(DocumentKey(frame.Reference.Partition, frame.Reference.Collection, frame.Reference.Id));
            if (record is not null)
            {
                EntityReferenceLifetime.RequireStored(charged, record.Reference, targetResource);
            }
            var document = record is null || record.Deleted || !Authorization.CanReadRow(principal, record.Access)
                || !Authorization.CanReadRow(frame.Principal, record.Access) ? null
                : ProjectWithAuthorityIntersection(frame.Principal, frame.Resource, principal, targetResource, record).Value;
            var appliedBytes = charged.ReadOwnedValue(KeySpace.AppliedBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
            var applied = NativeSerialization.Deserialize<long>(appliedBytes);
            if (applied < DocumentTokenInitialPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
            }
            var identity = Store.Identity;
            work.Check();
            return new OwnedDocumentReadResultV1(document, identity.NodeId, identity.Incarnation, identity.ReadGeneration,
                placement, principal.PolicyEpoch, applied, Store.Position);
        });
}
