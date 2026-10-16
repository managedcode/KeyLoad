using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 BeginResourceRetirement(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementBeginBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        ResourceRetirementValidation.Request(body.Request, Limits);
        if (phase.RetirementId != body.Request.RetirementId || operation.Id != body.Request.RetirementId)
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
        var preparation = RequireRetirementPreparationForBegin(transaction, body);
        var catalog = ReadInventoryPhysicalCatalog(transaction);
        var directory = PhysicalOwnerDirectorySerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        var digest = NativeOperationFingerprint.Compute(operation);
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementProtocol.Version,
            body.Request.RetirementId, ResourceRetirementPhase.Begin, operation.Id, lifetime,
            catalog.DefaultShard, position, digest);
        var control = new ResourceRetirementControlV1(body.Request, definition, lifetime, catalog, directory,
            ReadInventoryPlacementRevision(transaction), principal.Id, principal.PolicyEpoch, receipt, digest)
        {
            WorkOrigins = ResourceRetirementWorkOriginValidation.Capture(body.OwnerStatuses, directory, Limits)
        };
        var state = new ResourceRetirementStateV1(control, ResourceRetirementStage.Retiring,
            ImmutableArray<ResourceRetirementPhaseReceiptV1>.Empty, null, null);
        ResourceRetirementStorage.WriteState(transaction, state, Limits);
        ResourceRetirementStorage.CreateBlocker(transaction, lifetime, Limits);
        ResourceRetirementPreparationStorage.CompleteSource(transaction, preparation, control, Limits);
        return new(receipt, state, null);
    }
}
