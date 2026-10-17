using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Retains the actual catalog commit identity instead of relabelling a public parent request.</summary>
internal static class ResourceRetirementFinalizationReceiptValidation
{
    internal static void Require(ResourceRetirementFinalizationV1 completed, DatabaseLimits limits)
    {
        var canonical = completed.CanonicalReceipt;
        var original = completed.Original;
        var owner = completed.Control.Catalog.DefaultShard;
        if (completed.PolicyEpoch < ResourceRetirementProtocol.Empty || canonical is null || canonical.Token is null || original is null
            || original.Kind != OperationKind.ResourceRetirementPhase || original.Id != completed.Receipt.CommandId
            || string.IsNullOrWhiteSpace(original.PrincipalId) || original.NativePayload.IsEmpty
            || canonical.CommandId != completed.Receipt.CommandId
            || canonical.Token.Incarnation != owner.Incarnation
            || canonical.Token.AtomicPartitionId != NativeCatalogRoute.PartitionId
            || canonical.Token.Position != completed.Receipt.AppliedPosition
            || canonical.Token.OwnershipEpoch != owner.PlacementEpoch
            || canonical.Mutations.IsDefault || !canonical.Mutations.IsEmpty
            || canonical.Durability is not (DurabilityProfile.QuorumProcessDurable or DurabilityProfile.QuorumDurable))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        if (phase is null || phase.Version != ResourceRetirementProtocol.Version
            || phase.Phase != ResourceRetirementPhase.FinalizeCatalog
            || phase.RetirementId != completed.Control.Request.RetirementId || phase.Body.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        var body = NativeSerialization.Deserialize<ResourceRetirementFinalizeCatalogBodyV1>(phase.Body.Span);
        if (body is null || !ResourceRetirementValidation.Same(body.Control, completed.Control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementValidation.Bound(original, limits);
        ResourceRetirementValidation.Bound(canonical, limits);
    }
}
