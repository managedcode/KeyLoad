using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult PrepareResourceRetirement(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPrepareBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        ResourceRetirementValidation.Request(body.Request, Limits);
        if (operation.Id != ResourceRetirementIdentity.PreparationCommandId(body.Request) || phase.RetirementId != body.Request.RetirementId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var (definition, lifetime) = ReadInventoryResourcePair(transaction, body.Request.TenantId,
            body.Request.DatabaseId, body.Request.ResourceName, initial: true);
        if (lifetime.Incarnation != body.Request.ExpectedIncarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        ResourceRetirementStorage.RequireOpen(transaction, lifetime, Limits);
        var catalog = ReadInventoryPhysicalCatalog(transaction);
        var directory = PhysicalOwnerDirectorySerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Missing);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        var digest = NativeOperationFingerprint.Compute(operation);
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementProtocol.Version, body.Request.RetirementId,
            phase.Phase, operation.Id, lifetime, catalog.DefaultShard, position, digest);
        var control = new ResourceRetirementPreparationControlV1(body.Request, definition, lifetime, catalog, directory,
            principal.Id, principal.PolicyEpoch, [], receipt, digest);
        var state = new ResourceRetirementPreparationStateV1(control, ImmutableArray<ResourceRetirementOwnerReservationV1>.Empty, null)
        {
            OwnerAttempts = []
        };
        var result = new ResourceRetirementPhaseResultV1(receipt, null, null) { PreparationState = state };
        ResourceRetirementValidation.Bound(result, Limits);
        ResourceRetirementPreparationStorage.CreateSource(transaction, state, Limits);
        return Result(result);
    }
}
