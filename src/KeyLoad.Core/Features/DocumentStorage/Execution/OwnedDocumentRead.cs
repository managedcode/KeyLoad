using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string DocumentIdentityTenantMismatch = "The receiving principal belongs to another tenant.";
    private const string DocumentOwnerNodeMismatch = "The receiving document owner is not the admitted physical shard.";

    private DocumentResult? ReadDocumentAtCut(IKeyValueView view, PrincipalRecord principal,
        EntityRef reference, CommitToken? minimumToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        if (AtomicPartitionPlacementSerialization.ReadRow(view, reference.Partition) is not null)
        { _ = ReadPlacementWitness(view, reference.Partition); }
        if (minimumToken is not null)
        { ValidateDocumentSessionToken(view, reference.Partition, minimumToken); }
        var resource = Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        var record = view.GetRecord<DocumentRecord>(DocumentKey(reference.Partition, reference.Collection, reference.Id));
        var result = record is null || record.Deleted || !Authorization.CanReadRow(principal, record.Access)
            ? null : Project(principal, resource, record);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Reads a receiving-owner document and its private witness in one authorized storage cut.</summary>
    /// <param name="principalId">Identity verified by the configured peer boundary.</param>
    /// <param name="tenant">Expected canonical identity tenant, never a grant.</param>
    /// <param name="request">The original document scope and minimum token.</param>
    /// <param name="expectedOwner">The exact configured receiving physical owner.</param>
    /// <param name="cancellationToken">Original bounded operation cancellation.</param>
    /// <returns>The projected document and the actual receiving cut.</returns>
    public OwnedDocumentReadResultV1 ReadOwnedDocument(string principalId, string tenant,
        GetDocumentRequest request, PhysicalShardRecord expectedOwner, CancellationToken cancellationToken)
        => Store.Read<OwnedDocumentReadResultV1>(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
            { throw Errors.Fail(ErrorCode.PermissionDenied, DocumentIdentityTenantMismatch); }
            Authorization.Require(principal, request.Reference.Partition, request.Reference.Collection, Capability.DocumentsRead);
            var placement = ReadPlacementWitness(view, request.Reference.Partition);
            if (placement.PhysicalShardId != expectedOwner.PhysicalShardId
                || placement.Incarnation != expectedOwner.Incarnation
                || placement.PlacementEpoch != expectedOwner.PlacementEpoch
                || !placement.VoterIds.SequenceEqual(expectedOwner.VoterIds, StringComparer.Ordinal))
            { throw Errors.Fail(ErrorCode.OwnershipLost, DocumentOwnerNodeMismatch); }
            var document = ReadDocumentAtCut(view, principal, request.Reference, request.MinimumToken, cancellationToken);
            var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
            var applied = NativeSerialization.Deserialize<long>(appliedBytes);
            if (applied < DocumentTokenInitialPosition)
            { throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition); }
            var identity = Store.Identity;
            cancellationToken.ThrowIfCancellationRequested();
            return new(document, identity.NodeId, identity.Incarnation, identity.ReadGeneration,
                placement, principal.PolicyEpoch, applied, Store.Position);
        });
}
