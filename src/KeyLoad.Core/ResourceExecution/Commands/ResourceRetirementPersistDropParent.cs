using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 PersistResourceRetirementDropParent(IAtomicTransaction tx,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPersistDropParentBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        ResourceRetirementDropParentStorage.RequireLookup(body.Lookup, Limits);
        ResourceRetirementValidation.Request(body.Expected, Limits);
        var previous = ReadRetirementDropParent(tx, body.Lookup, principal.Id);
        if (previous is not null)
        {
            // Only the outer original-outcome path may acknowledge an already persisted phase.
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }
        var request = body.Expected;
        if (request.RetirementId != body.Lookup.CommandId || phase.RetirementId != request.RetirementId
            || request.TenantId != body.Lookup.Request.TenantId || request.DatabaseId != body.Lookup.Request.DatabaseId
            || request.ResourceName != body.Lookup.Request.ResourceName)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var (definition, lifetime) = ReadInventoryResourcePair(tx, request.TenantId,
            request.DatabaseId, request.ResourceName, initial: true);
        if (definition.Kind != ResourceKind.Collection || definition.RelationalSchema is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        if (lifetime.Incarnation != request.ExpectedIncarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        var owner = ReadInventoryPhysicalCatalog(tx).DefaultShard;
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementProtocol.Version, request.RetirementId,
            ResourceRetirementPhase.PersistDropParent, operation.Id, lifetime, owner, position, NativeOperationFingerprint.Compute(operation));
        var parent = new ResourceRetirementDropParentV1(ResourceRetirementProtocol.Version, body.Lookup, principal.Id,
            principal.PolicyEpoch, RetirementDropFingerprint(body.Lookup, principal.Id), definition, lifetime,
            request, operation, receipt, null, null, null, null);
        RequireRetirementDropParentShape(parent);
        RequireRetirementDropParentOriginal(parent, operation, receipt, ResourceRetirementPhase.PersistDropParent);
        ResourceRetirementDropParentStorage.Write(tx, parent, Limits);
        return new(receipt, null, null)
        {
            DropParent = parent
        };
    }
}
