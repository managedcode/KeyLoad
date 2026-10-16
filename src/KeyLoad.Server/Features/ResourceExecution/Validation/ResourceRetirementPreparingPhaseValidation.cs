using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparingPhaseValidation
{
    internal static void Require(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase,
        DatabaseLimits limits)
    {
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.PrepareRetirement:
                var prepare = NativeSerialization.Deserialize<ResourceRetirementPrepareBodyV1>(phase.Body.Span);
                if (prepare?.Request is null || prepare.Request.RetirementId != phase.RetirementId)
                {
                    throw Invalid();
                }
                RequireSource(outer);
                break;
            case ResourceRetirementPhase.ReserveOwner:
                var reserve = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span);
                if (reserve?.Context is null || reserve.Context.Purpose != ResourceRetirementAttemptPurpose.OwnerReservation
                    || reserve.Context.Receiver is null || !PhysicalOwnerEntryValidation.Same(reserve.Context.Receiver, outer.Destination))
                {
                    throw Invalid();
                }
                ResourceRetirementDurablePhaseValidation.RequireContext(outer, reserve.Context, limits);
                break;
            case ResourceRetirementPhase.RecordReservation:
                var record = NativeSerialization.Deserialize<ResourceRetirementRecordReservationBodyV1>(phase.Body.Span);
                if (record?.Control is null || record.Reservation is null)
                {
                    throw Invalid();
                }
                RequireControl(outer, record.Control, limits);
                RequireSource(outer);
                break;
            default:
                throw Invalid();
        }
    }

    internal static void RequireBegin(ResourceRetirementNativeRequest outer, ResourceRetirementBeginBodyV1 body,
        DatabaseLimits limits)
    {
        if (body.Preparation is null)
        {
            throw Invalid();
        }
        RequireControl(outer, body.Preparation, limits);
    }

    internal static void RequireControl(ResourceRetirementNativeRequest outer,
        ResourceRetirementPreparationControlV1 control, DatabaseLimits limits)
    {
        ResourceRetirementPreparationValidation.Control(control, limits);
        if (!PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, outer.Control.Owner))
        {
            throw Invalid();
        }
    }

    private static void RequireSource(ResourceRetirementNativeRequest outer)
    {
        if (!PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
